import { element, label, PAGE_ROOT } from "../../../src/surface/src/isolation/index.ts";
import { arrowCommand } from "../../canvas/src/layout.ts";
import { render } from "../../canvas/src/render.ts";
import { commitControl, diagnosticList, editNotice, RiderSurface } from "./client.ts";

declare global { interface Window { weavesharpReceive(encoded: string): void } }

const root = document.getElementById(PAGE_ROOT);
if (root === null) throw new Error("Missing graph container.");
const mount = root;
const surface = new RiderSurface();
let activeView = ".ws-outline";

function restoreFocus(): void {
  const items = mount.querySelector(activeView)?.querySelectorAll<HTMLElement>("[data-ws-key]") ?? [];
  const selected = [...items].find((item) => item.getAttribute("data-ws-key") === surface.session?.focus);
  (selected ?? mount.querySelector<HTMLElement>("[role=tree]"))?.focus();
}

function draw(focus = false): void {
  const nodes = [
    element(document, "p", { id: "editing-note" }, [label(document, editNotice)]),
    commitControl(document),
    element(document, "p", {}, [label(document, "Use arrow keys to explore the diagram and outline. Enter expands a focused region.")]),
    element(document, "p", { role: "status", "aria-live": "polite" }, [label(document, surface.notice)]),
  ];
  const session = surface.session;
  if (session !== undefined) {
    nodes.push(diagnosticList(document, session.graph));
    const views = render(document, session.graph, {
      focus: session.focus, selected: session.selected, collapsed: session.collapsed, announcement: session.announcement,
    }, "rider");
    nodes.push(views.canvas, views.outline);
  }
  mount.replaceChildren(...nodes as HTMLElement[]);
  if (focus) restoreFocus();
}

mount.addEventListener("focusin", (event) => {
  const item = event.target instanceof Element ? event.target.closest("[data-ws-key]") : null;
  const identity = item?.getAttribute("data-ws-key");
  if (identity != null) {
    surface.session?.focusEntity(identity);
    activeView = item?.closest(".ws-canvas") == null ? ".ws-outline" : ".ws-canvas";
  }
});
mount.addEventListener("keydown", (event) => {
  if (event.altKey || event.ctrlKey || event.metaKey) return;
  const session = surface.session;
  const movement = arrowCommand(event.key, "left-to-right");
  if (movement !== undefined) session?.move(movement);
  else if (event.key === "Enter" && session?.inspect()?.kind === "region") session.toggleRegion(session.inspect()!.id);
  else if (event.key.toLowerCase() === "i") surface.commit();
  else return;
  event.preventDefault();
  draw(true);
});

window.weavesharpReceive = (encoded) => {
  const focused = mount.contains(document.activeElement) && document.activeElement?.closest("[data-ws-key], [role=tree]") != null;
  surface.receive(encoded);
  draw(focused);
};
draw();
