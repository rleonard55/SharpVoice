# Gemini subscription support through Antigravity

Approved by the user: “Use Antigravity with Google sign-in.”

Add Gemini via Google’s official Antigravity CLI alongside ChatGPT and Claude, using Google-owned browser sign-in and credentials. Use an available Flash model by default, show the selected model, and route dictation, selection editing and Ask AI through existing prompts. No API-key billing fallback. Selecting other providers preserves existing settings and behavior. Missing CLI or unavailable login should provide actionable setup guidance. Account controls follow the existing single-control pattern; do not invent login/status command contracts. Google’s shared credential lifecycle and service allowance apply.

Approaches: the old Gemini CLI consumer login was retired; direct API keys do not satisfy subscription sign-in; the approved approach is the official Antigravity CLI. Its native runtime must be inspected before defining version requirements, account commands and request isolation. Do not grant a text-refinement request access to tools or user files; avoid inherited hooks/MCP/plugins and persistent speech logs. If the official interface cannot enforce this, document the concrete limitation rather than presenting an unverified integration as working.
