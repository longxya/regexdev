# [regexdev](https://www.regexdev.com/)

A visualizer, debugger, and editor for **C#-style regex**.

`regexdev` focuses on **making the internal structure and matching process of regex visible**.  
It parses regular expressions into an AST, visualizes them, and allows step-by-step debugging of the matching process.

---

## 🎬 Demonstration
### All functions
![Demonstration3](https://github.com/user-attachments/assets/5fb46bb8-258e-4219-9b69-07c63e19d100)

### Parsing
![Parse1](https://github.com/user-attachments/assets/f2290d6d-6e3d-402f-b090-ee5dfb8357fc)

### Debugging
![Debug1](https://github.com/user-attachments/assets/bba17b5a-f62d-4b43-932e-af3fefae38fb)

### Editing
![Edit](https://github.com/user-attachments/assets/69cc5756-aae2-41fa-bb4c-23753b33fd72)

### Multi-Display
![MultiDisplay1](https://github.com/user-attachments/assets/85f9fe48-7956-4f33-99da-ca4261d03f42)

### Breakpoints
![Breakpoints](https://github.com/user-attachments/assets/f8ef9030-f84c-42c2-b131-2fa23e8d0d28)

---

## 🚀 Features

- 🔍 Regex **AST visualizer**
- 🐞 Step-by-step **regex debugger**
- ✏️ Interactive **regex editor**
- 🧩 **Breakpoints** in the matching process
- 🖥️ **Multi-panel / multi-view** display
- 🎞️ Matching process playback

---

## ✅ Verification & Correctness

### How does `regexdev` ensure the matching process is correct?

The matching results are continuously verified against the official **C# regex engine**:

1. Compare **Index** and **Length** of **every capture in every group**
2. Compare the total number of `Matches`
3. If **any difference** is detected, a warning will be shown immediately

This makes `regexdev` suitable not only for visualization, but also for **learning, debugging, and validating complex regex behavior**.

---

## 🏷️ Tags / Capabilities

- Regex Visualizer  
- Regex Debugger  
- Regex Editor  
- Regex Breakpoint System  
- Multi-View Regex Inspection  

---

## 🧠 About the Code

> ⚠️ **Important note**

This repository currently contains **only the regex parser**.

The full `regexdev` tool also includes a matcher, debugger runtime, and UI layers, but those parts are **not open-sourced yet** due to ongoing refactoring and cleanup.

### Why only the parser?

This project originally started as an experimental tool, and the early matching implementation was tightly coupled and difficult to maintain.  
Recently, I redesigned and rewrote the **regex parser** from scratch with a much cleaner architecture:

- Explicit **AST-based design**
- Clear separation of parsing stages
- Easier to inspect, extend, and visualize

As a result, the parser became the **most stable and reusable part** of the project, and is published independently.

### Especially:

I wrote a [C# regex](https://github.com/longxya/regexdev/blob/main/RegexDev/RegexParse.cs#L933) to replace the lexer and handle most grammar checks.

I didn’t want to write a lexical analyzer. 🚫

### Extension point

If you want to experiment with your own parsing logic or build additional tooling on top of it, the main entry point is:

```csharp
new RegexParse().Parse(...)
```
This allows you to focus on analysis, visualization, or matching logic,  
without having to implement a regex lexer from scratch.

---

## 💻 Console Demo

A minimal console application is included to demonstrate the parser behavior.

Type a regex expression, then enter `parse` on a new line and press `Enter`
to see the parsed AST output.
<img width="1505" height="1012" alt="3ac9b184-e044-4ee9-bff8-aff03b881621" src="https://github.com/user-attachments/assets/bee3112d-642c-46ca-a4da-3075428b834f" />
<img width="1884" height="932" alt="bf41a97a-9685-4457-bf99-2707d6b2404b" src="https://github.com/user-attachments/assets/132f7d72-e9cd-4728-a0bc-7bd48ceddad6" />
<img width="1888" height="864" alt="5cb489d9-2d4b-4105-9c55-23e1ffbcfb14" src="https://github.com/user-attachments/assets/8f00ef02-ebf7-42ae-a76e-eda44ccac779" />

---

## 📢 Feedback, Suggestions, and Discussions are Very Welcome! 

I’d love to hear your thoughts on this project. If you have any suggestions, issues, or feature requests, please feel free to open an issue or contribute to the discussion.

- **[Open an Issue](https://github.com/longxya/regexdev/issues)**
- **[Start a Discussion](https://github.com/longxya/regexdev/discussions)**

Your feedback will help make this project even better! 🚀

---

## 📄 License

[MIT](https://github.com/longxya/regexdev/blob/master/LICENSE)
