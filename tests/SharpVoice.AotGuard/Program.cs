// Intentional negative compilation fixture. This unsupported upstream API uses
// reflection. Native publish MUST fail, proving library IL compilation has not
// hidden reachable AOT/trim problems from the strict native entrypoint.
Console.WriteLine(new LLama.Common.ChatHistory().ToJson());
