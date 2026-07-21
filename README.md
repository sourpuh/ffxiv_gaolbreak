<h1><img align="center" height=100 src="./ReadmeImages/icon.png" alt="Thanks Leonhart for the Icon!">  Gaolbreak</h1>

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin that breaks FFXIV's UI out of its gaol. Gaolbreak captures the native game UI and displays it as a plugin window.

## Features
<img align="right" height=350 src="./ReadmeImages/gaolbreak.png">

* Layer game windows with plugin windows
* Allow 3D overlay plugins to draw under the game's UI
* Prevent ReShade or other graphics effects from affecting the UI
* "Indicator" dot on top left shows current status
  * Green - Enabled
  * Yellow - Inactive due to game state
  * Orange - Self Disabled (requires plugin update)
  * Red - User Disabled (killswitched)
  * Left click toggles killswitch
  * Right click opens the plugin config
<br clear="right"/>

## Pins
Some plugins create ImGui windows that are intended to appear "attached" to native UI Addons, such as ImGui overlay buttons or attached side windows. Without Gaolbreak, these plugins will always draw on top of the native UI. With Gaolbreak, these can get lost underneath Addons or have confusing layering. You can pin plugin windows above the native UI in Gaolbreak's config. If you report these plugins, they can be fixed for everyone; follow these steps:

1. Open Gaolbreak's config UI.
2. Go to the "Windows" tab.
3. Identify the plugin window; when you click on it, it will appear at the top of the list.
4. Note the window ID.
5. Go to the "Addons" tab.
6. Identify the addon window that the plugin window should be attached to.
7. Note the Addon's name.
8. Create a new GitHub issue or message me on Discord. Provide the following:
    1. The window ID(s)
    2. The Addon name(s)
    3. Any other context that might be necessary, such as if the plugin doesn't attach to a specific window.

As an example of what you need to provide, WaymarkPresetPlugin's window ID is `0x3E3947D9` and it attaches to the Addon named `FieldMarker`.

## Known issues
* Windows pinned to specific addons are hidden if any other addon is brought to the Foreground, even if the window is not occluded
  * This will be fixed with occlusion checks and/or splitting the FG into multiple windows.

## Support
If you found a bug or have suggestions for the plugin, please do one of the following:

1. Check if a [GitHub issue](https://github.com/sourpuh/ffxiv_gaolbreak/issues) already exists for the same thing.
1. Create a [new GitHub issue](https://github.com/sourpuh/ffxiv_gaolbreak/issues/new). Provide a detailed description of the suggestion or problem (For bugs, include logs or steps to reproduce the issue).
1. Ask in Dalamud Discord: it might be a known issue or people might be able to help you quickly.

## About
Gaolbreak's implementation uses the following primary components:

1. Capturer + Capture Injector - Redirects the UI RenderCommands from targetting the game's BackBuffer to textures owned by Gaolbreak. The capture injector tracks which draw commands correspond to which addon and injects special clipmask draw commands that are later converted to SetRenderTarget calls to the captures.
2. Window Manager + UI Overlay Window - These work together to direct hovers and clicks to the appropriate native or ImGui components, and to bring windows to the foreground when necessary. Most plugin windows will draw above the background. Those that wish can use ImGui methods to send to the back of the draw order to draw under the UI.
