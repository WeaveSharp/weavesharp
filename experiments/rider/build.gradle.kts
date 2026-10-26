plugins {
    alias(libs.plugins.kotlin.jvm)
    alias(libs.plugins.intellij.platform)
}

group = "com.weavesharp"
version = providers.gradleProperty("pluginVersion").get()

repositories {
    mavenCentral()
    intellijPlatform { defaultRepositories() }
}

dependencies {
    intellijPlatform {
        rider(libs.versions.rider.get()) { useInstaller = true }
        bundledPlugin("com.intellij.modules.jcef")
    }
    testImplementation(libs.junit.jupiter)
    testRuntimeOnly(libs.junit.launcher)
}

kotlin { jvmToolchain(21) }

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            sinceBuild = "262"
            untilBuild = "262.*"
        }
    }
}

val repository = layout.projectDirectory.dir("../..").asFile
val compileSurface by tasks.registering(Exec::class) {
    workingDir(repository)
    commandLine("node", "node_modules/typescript/bin/tsc", "-p", "experiments/rider")
    inputs.files(fileTree("webview"), fileTree("test"), file("tsconfig.json"),
        fileTree("../canvas/src"), file("../vscode/src/schemas.generated.ts"), fileTree("../../src/surface/src"))
    inputs.file("../../src/surface/tsconfig.json")
    outputs.dir("out/experiments")
    outputs.dir("out/src")
}
val generateSurface by tasks.registering(Exec::class) {
    dependsOn(compileSurface)
    workingDir(repository)
    commandLine("node", "experiments/rider/out/experiments/rider/webview/generate.js")
    inputs.dir("out/experiments")
    inputs.dir("out/src")
    inputs.file("../canvas/src/canvas.css")
    outputs.dir("out/resources")
}
tasks.processResources {
    dependsOn(generateSurface)
    from("out/resources")
}
tasks.test { useJUnitPlatform() }
