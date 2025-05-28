# FsOperator

**FsOperator** is an F# sample application that utilizes the OpenAI [responses API](https://platform.openai.com/docs/api-reference/responses) to enable a *computer use agent*.

It currently includes a *partial implementation* of the **responses** API, which will likely become the dominant interface over time—combining capabilities from both *chat* and *assistant* APIs.

The application uses the new OpenAI [computer_use_preview](https://platform.openai.com/docs/guides/tools-computer-use) generative model as its core. This model operates in an observe-act-observe loop: it visually interprets a browser page and issues commands (*click*, *scroll*, *type*, etc.) to accomplish tasks based on user instructions.

Below is a demonstration an Amazon search session to find a specific type of cell phone case:

![screenshot](imgs/amazonshop.gif)

---

### ⚠️ Caveats

> *At the current level of technology, human supervision is required—though the future looks very promising.*

> CUA model's instruction following ability seems to be somewhat limited so it need to be paired with another model for better reasoning and instruction following. This is a planned future enhancement.
---

### 🚀 Application Usage

- Ensure **.NET 9.x runtime or SDK** is installed
- Set the `OPENAI_API_KEY` environment variable to provide your OpenAI API key. The key should have access to the OpenAI CUA model.
- Install Playwright Chromium browser component. Typically you need to install [*node.js*](https://nodejs.org) and then  use the command ```npx install playwright```

- Text chat mode has been tested successfully on Windows and MacOS.
- *Voice chat mode currently works on Windows only (the WebRTC library for MacOS is yet to be included)*

#### Steps:

1. **Enter a URL** in the top address bar (must include `https://`; e.g., `https://amazon.com`).
2. **Hit Enter** to navigate. Log in manually if required.
3. **Input task instructions** in the instructions box.
4. **Click 'Start Task'** to begin the task.
5. **Alternatively, use the voice mode** to directly chat with the OpenAI real-time voice assistant to generate and execute instructions for CUA model.
6. **Observe computer actions** issued by the model on right end of the address bar. Any warnings, status or error messages are shown at the bottom.
7. **See log messages** by expanding the panel on the right. It shows all API messages that are sent and received.

> Note: the browser screen shakes when a snapshot it taken. This seems to be coming from Chromium itself - not sure if there is a fix available.

The model may issue **security warnings** that ideally should be acknowledged by a human. Currently, the app implicitly acknowledges these warnings to allow the process to continue uninterrupted. They are shown in the status bar.

- The process continues until the model returns an **assistant message** (completing its "turn").
- The resulting message is displayed in the **Chat** box.
- You may respond to the message and continue the task further
- Or cancel the task by clicking **Cancel Task**

> ⚠️ *Caveat: This is beta code. It has not been fully battle-tested.*

---

Release Notes
- 2025-05-27 - Make OpenAI realtime api voice connection work on macos with webrtc 
