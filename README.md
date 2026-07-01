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

## Installation
* Repository: https://puni.sh/api/repository/sourpuh
* If you don't know how to use custom repositories, follow "Download" instructions on https://puni.sh/directory/sourpuh/gaolbreak

## Known issues
* TintAdjust effects (Gamma and Color Filters) do not apply to Gaolbreak UI like it does the native UI.
* Pieces of a few Addons that multiply the game's color (such as the duty actionbar) may appear darker.
* Gaolbreak disables ReShade with REST - if using REST, you must disable the "UI" toggle group (open ReShade > Add-ons > List of Toggle Groups> UI)

## Support
If you found a bug or have suggestions for the plugin, please do one of the following:

1. Check if a [GitHub issue](https://github.com/sourpuh/ffxiv_gaolbreak/issues) already exists for the same thing.
1. Create a [new GitHub issue](https://github.com/sourpuh/ffxiv_gaolbreak/issues/new). Provide a detailed description of the suggestion or problem (For bugs, include logs or steps to reproduce the issue).
1. Ask in [Puni.sh Discord](https://discord.gg/punishxiv): it might be a known issue or people might be able to help you quickly.

## About
Gaolbreak's implementation uses the following primary components:

1. Capturer + Capture Injector - Redirects the UI RenderCommands from targetting the game's BackBuffer to textures owned by Gaolbreak. The capture injector tracks which draw commands correspond to which addon and injects special clipmask draw commands that are later converted to SetRenderTarget calls to the captures.
2. Window Manager + UI Overlay Window - These work together to direct hovers and clicks to the appropriate native or ImGui components, and to bring windows to the foreground when necessary. Most plugin windows will draw above the background. Those that wish can use ImGui methods to send to the back of the draw order to draw under the UI.
