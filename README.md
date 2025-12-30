# regexdev

A visualizer, debugger, and editor for **C#-style regex**.

`regexdev` focuses on **making the internal structure and matching process of regex visible**.  
It parses regular expressions into an AST, visualizes them, and allows step-by-step debugging of the matching process.

---

## 🎬 Demonstration
### All functions
![Demonstration1](https://github.com/user-attachments/assets/15e92004-6050-4d33-a653-c9fba27d7eab)

### Parsing
![Parse](https://github.com/user-attachments/assets/e5b7be99-3d11-4aae-9efe-528e032d3ccf)

### Debugging
![Debug](https://github.com/user-attachments/assets/add74fc5-68f0-4804-9bf7-b460842f146f)

### Editing
![Edit](https://github.com/user-attachments/assets/69cc5756-aae2-41fa-bb4c-23753b33fd72)

### Multi-Display
![MultiDisplay](https://github.com/user-attachments/assets/edc4ebd7-4d45-46e5-8671-424c5c9163e8)

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

### Extension point

If you want to experiment with your own parsing logic or build additional tooling on top of it, the main entry point is:

```csharp
new RegexParse().Parse(...)
```
This allows you to focus on analysis, visualization, or matching logic,  
without having to implement a regex lexer from scratch.

---

## 📄 License

[MIT](https://github.com/longxya/regexdev/blob/master/LICENSE)
