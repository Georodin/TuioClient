# Beyond TUIO Client

Standalone canvas-oriented TUIO 1.1 client and debug token UI for Beyond projects.

## Setup

Use `GameObject > Beyond TUIO Client > TUIO 1.1 Canvas Debug Setup` to create:

- a Beyond TUIO 1.1 session using UDP or WebSocket with configurable IP address and port
- a screen-space `BF_TUIO_Canvas` if the scene does not already contain a canvas
- an event system if needed
- a `TUIO 1.1 Debug Tokens` root with UI images and labels for token IDs `1..20`
- a runtime status panel with a debug visibility toggle button

Open `Window > Beyond TUIO Client > TUIO Client` for connection settings, recent activity, and debug token show/hide controls.

## Runtime Debug Tokens

Debug tokens start `FREE` by default.

- Drag a visible debug token to move it.
- Use the mouse wheel over a token to rotate it.
- Right-click a token to toggle `PLACED` / `FREE`.
- Press `F9` at runtime, or use the canvas toggle button, to show/hide debug token visibility.

Live TUIO object messages override the matching token pose while the object is active. The debug status panel reports the configured endpoint and switches to `LIVE TUIO` when recent real TUIO object messages are received.

## Packaging Notes

This folder is a UPM-style package. It carries the required TuioNet/logging runtime DLLs locally so it does not depend on the InteractiveScape Unity package.