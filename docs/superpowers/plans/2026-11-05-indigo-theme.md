# Indigo Theme Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement these tasks in the current workspace.

**Goal:** Apply the selected Blue + Indigo direction to the desktop app and IDE panel.
**Architecture:** Extend the existing shared COLORREF palette and owned GDI brushes, then paint the header/tab accents and semantic status colors. Style the Webview with matching dark/light tokens and explicit high-contrast fallbacks.
**Tech Stack:** C#, Win32 GDI, Webview HTML/CSS/JavaScript.
**Spec:** docs/superpowers/specs/2026-11-05-indigo-theme-design.md

## Global Constraints
- Preserve existing layouts, commands, mic ownership and request behavior.
- Preserve OS light/dark switching and IDE high-contrast colors.
- No new runtime dependencies. No GitHub publication without another release request.

- [x] Add semantic palette contrast checks; implement desktop palette, brand header, tab accents and mic colors.
- [x] Style matching IDE surfaces, primary buttons, questions and recording state; verify existing panel workflows.
- [x] Build/test, inspect native light/dark screenshots and IDE preview, package matching VSIX, publish native app locally and verify/stage delivery.

- [x] Reproduce the missing banner graphic with a failing native pixel regression; implement continuous gradient/wave rendering behind owner-drawn native title/tagline labels and verify light/dark switching.
