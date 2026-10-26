import { element, label, PAGE_ROOT } from "../../../src/surface/src/isolation/index.ts";
import { render } from "../../canvas/src/render.ts";
import { arrowCommand } from "../../canvas/src/layout.ts";
import { SurfaceClient } from "./surface-client.ts";

declare function acquireVsCodeApi(): {
  postMessage(json: string): void;
  getState(): { readonly load: number } | undefined;
  setState(value: { readonly load: number }): void;
};

const api = acquireVsCodeApi();
const load = (api.getState()?.load ?? 0) + 1;
api.setState({ load });
const client = new SurfaceClient(`page-${load}`, (json) => api.postMessage(json));
const root = document.getElementById(PAGE_ROOT);
if (root === null) throw new Error("The surface root is missing.");
const mount = root;
let focusView = ".ws-outline";

function button(control: string, caption: string, action: () => void, disabled = false): HTMLButtonElement {
  const node = element(document, "button", { type: "button", "data-ws-control": control, ...(disabled ? { disabled: "disabled" } : {}) },
    [label(document, caption)]) as HTMLButtonElement;
  node.addEventListener("click", () => { action(); redraw(true); });
  return node;
}
function focusGraph(): void {
  const key = client.session?.focus;
  const view = mount.querySelector(focusView);
  const target = [...(view?.querySelectorAll<HTMLElement>("[data-ws-key]") ?? [])]
    .find((node) => node.getAttribute("data-ws-key") === key) ?? mount.querySelector<HTMLElement>("[role=tree]");
  target?.focus();
}
function redraw(moveFocus = false): void {
  const active = document.activeElement;
  const control = mount.contains(active) ? active?.closest("[data-ws-control]")?.getAttribute("data-ws-control") : undefined;
  const session = client.session;
  const children: Node[] = [element(document, "p", {}, [label(document,
    "C# stand-in: arrows browse statements and pins. I previews an empty statement; Escape cancels.")]) as HTMLElement];
  children.push(button("insert", "Insert empty statement", () => client.prepare(), client.busy || session?.graph.coverage !== "complete"));
  if (client.proposal !== undefined) {
    const preview = client.proposal;
    children.push(element(document, "div", { role: "group", "aria-label": "Source preview" }, [
      label(document, preview.description), ...preview.changes.map((change) => element(document, "p", {}, [
        label(document, `At UTF-16 offset ${change.span.start}, replace ${change.span.length} units with: `), label(document, change.text),
      ])), button("apply", "Apply", () => client.confirm()), button("cancel", "Cancel", () => client.cancel()),
    ]) as HTMLElement);
  }
  children.push(element(document, "p", { role: "status", "aria-live": "polite" }, [label(document, client.notice)]) as HTMLElement);
  if (session !== undefined) {
    const views = render(document, session.graph, { focus: session.focus, selected: session.selected,
      collapsed: session.collapsed, proposals: session.proposals(), announcement: session.announcement }, "vscode");
    children.push(views.canvas as HTMLElement, views.outline as HTMLElement);
  }
  mount.replaceChildren(...children);
  if (moveFocus) focusGraph();
  else if (control !== undefined && control !== null) {
    const replacement = [...mount.querySelectorAll<HTMLButtonElement>("[data-ws-control]")]
      .find((node) => node.getAttribute("data-ws-control") === control && !node.disabled);
    if (replacement === undefined) focusGraph();
    else replacement.focus();
  }
}
mount.addEventListener("focusin", (event) => {
  const target = event.target instanceof Element ? event.target.closest("[data-ws-key]") : null;
  const key = target?.getAttribute("data-ws-key");
  if (key !== null && key !== undefined) {
    client.session?.focusEntity(key);
    focusView = target?.closest(".ws-canvas") === null ? ".ws-outline" : ".ws-canvas";
  }
});
mount.addEventListener("keydown", (event) => {
  if (event.altKey || event.ctrlKey || event.metaKey) return;
  if (event.key === "Escape") {
    client.cancel();
    event.preventDefault();
    redraw(true);
    return;
  }
  if (event.target instanceof HTMLButtonElement) return;
  const command = arrowCommand(event.key, "left-to-right");
  if (command !== undefined) client.session?.move(command);
  else if (event.key.toLowerCase() === "i") client.prepare();
  else return;
  event.preventDefault();
  redraw(true);
});
window.addEventListener("message", (event: MessageEvent<unknown>) => {
  const hadGraphFocus = document.activeElement?.closest("[data-ws-key], [role=tree]") != null && mount.contains(document.activeElement);
  client.read(event.data);
  redraw(hadGraphFocus);
});
redraw();
client.start();
