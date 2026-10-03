# SnapShot

Knowledge-projekt: snapshot

Windows-verktyg i .NET 10 med Native AOT: PrintScreen, dra en ruta, PNG sparas och sökvägen
kopieras till klippbordet. Se `README.md` och `plan.md`.

- Bygg: `dotnet build` (warnings as errors, Avig.Analyzers på error). Test: `dotnet test`.
- Installera: `./publish.ps1` (testar, publicerar till `G:\Apps\SnapShot`, autostart).
- Nya `.cs`-filer måste ha CRLF (AVIG0006); kör `unix2dos` om ett verktyg skrivit LF.
