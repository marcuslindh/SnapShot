# SnapShot — plan

Ett litet Windows-verktyg i .NET 10 för att snabbt ta skärmklipp som ska skickas till Claude.
Publiceras med Native AOT och följer kodkvalitetsreglerna i
`G:\OneDrive\SkunkWorks\Dev\2026\Medusa\docs\code-quality.md`.

## Mål

1. Tryck på **PrintScreen**.
2. Skärmen "fryses" och dimmas; dra en rektangel med musen.
3. När musknappen släpps sparas markeringen som PNG i `G:\OneDrive\bilder\Screenshots`.
4. Den fullständiga sökvägen till filen kopieras till klippbordet, redo att klistras in i Claude Code.

Utanför v1: redigering/annotering, bild till klippbordet, uppladdning, historik-UI.

## Native AOT — går det?

Ja, men inte med WinForms. WinForms (och `System.Drawing.Common`) är inte trimningssäkert;
`PublishAot` tillsammans med `UseWindowsForms` ger byggfel NETSDK1175. Det finns en
odokumenterad flagga som tystar felet, men då kraschar appen i stället vid körning — den
används inte.

Appen är liten nog att skrivas direkt mot Win32 via P/Invoke, och det är AOT-säkert:

| Behov | WinForms-vägen (används inte) | AOT-vägen (används) |
|---|---|---|
| Ikon i systemfältet | `NotifyIcon` | `Shell_NotifyIconW`, balloon-notis via `NIF_INFO` |
| Meny | `ContextMenuStrip` | `CreatePopupMenu` + `TrackPopupMenu` |
| Overlay-fönster | `Form` | `RegisterClassExW` + `CreateWindowExW`, WndProc som `[UnmanagedCallersOnly]`-funktionspekare |
| Skärmfångst | `Graphics.CopyFromScreen` | `BitBlt` till en 32-bitars DIB-sektion (`CreateDIBSection`) |
| Ritning | `Graphics` | GDI med minnes-DC (dubbelbuffring); den dimmade bilden räknas ut en gång direkt i DIB-minnet |
| PNG | `Bitmap.Save` | Egen PNG-kodare: `ZLibStream` + CRC32 från `System.IO.Hashing` |
| Klippbord | `Clipboard.SetText` | `OpenClipboard` / `SetClipboardData(CF_UNICODETEXT)` |
| Meddelandeloop | `Application.Run` | Egen `GetMessageW`-loop på huvudtråden |
| Inställningar | — | `System.Text.Json` med källgenerator (`JsonSerializerContext`) |

P/Invoke-deklarationerna genereras av **Microsoft.Windows.CsWin32** (källgenerator, ingen
reflektion) med `"allowMarshaling": false` i `NativeMethods.json`, så all interop är blittbar
och AOT-säker. Det slipper oss att handskriva ett fyrtiotal signaturer.

Vinst: en exe på några MB, ingen .NET-runtime krävs, start på några millisekunder och lågt
minnesavtryck för en process som alltid ligger i bakgrunden.

Kostnad: mer egen kod för fönster och ritning än med WinForms. Blir det ohanterligt är
reservplanen WinForms utan AOT, publicerat med `PublishReadyToRun` — det beslutet tas i så
fall efter etapp 3, inte i förväg.

## Teknikval

| Område | Val | Varför |
|---|---|---|
| Ramverk | .NET 10 (SDK 10.0.400, låst i `global.json`) | Senaste LTS. |
| Publicering | `PublishAot`, `win-x64`, `OutputType WinExe` | Se ovan. |
| Fånga PrintScreen | `SetWindowsHookExW(WH_KEYBOARD_LL)` | `RegisterHotKey` på `VK_SNAPSHOT` är opålitligt och kan inte svälja tangenten. |
| DPI | `PerMonitorV2` i `app.manifest` | Annars blir klippen suddiga eller felplacerade på skalade skärmar. |
| Filformat | PNG, RGB 8 bit | Förlustfritt, bra för text i skärmbilder. |
| Felhantering | `Avig.Result` (`Result` / `Result<T>`) | AVIG0007 förbjuder `null` som felsignal. |
| Tid | `TimeProvider` | AVIG0020 förbjuder `DateTime.Now`; filnamnet ska ändå ha lokal tid → `TimeProvider.System.GetLocalNow()`, utbytbart i test. |

## Kodkvalitet

Uppsättningen tas över från Medusa enligt `code-quality.md`, steg för steg.

### `Directory.Build.props` (repo-roten)

Samma analyzer-paket som Medusa, utan `Medusa.Analyzers` (dess regler förutsätter CQRS/Mediator
och skulle bara vara tysta här). `TreatWarningsAsErrors` läggs i props-filen i stället för
per csproj, så att inget projekt — inklusive testprojektet — kan glömma det.

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <!-- Alla med PrivateAssets="all" och
         IncludeAssets="runtime; build; native; contentfiles; analyzers" -->
    <PackageReference Include="SonarAnalyzer.CSharp" Version="10.30.0.144632" />
    <PackageReference Include="Meziantou.Analyzer" Version="3.0.126" />
    <PackageReference Include="Roslynator.Analyzers" Version="4.15.0" />
    <PackageReference Include="AsyncFixer" Version="2.1.0" />
    <PackageReference Include="Microsoft.VisualStudio.Threading.Analyzers" Version="18.7.23" />
    <PackageReference Include="Avig.Analyzers" Version="1.1.0" />
  </ItemGroup>
</Project>
```

`GenerateDocumentationFile` behövs för att doc-kommentarsregler (AVIG0011, CS1591 m.fl.) ska
kontrolleras i bygget.

### `.editorconfig`

Kopieras från Medusa (582 rader) inklusive konventions-överstyrningarna (explicita
bool-jämförelser tillåtna, explicita typer i stället för `var` via **IDE0008**, inte IDE0007).
`MEDUSA*`-raderna tas bort eftersom paketet inte används. Alla Avig-regler sätts till `error`:

```ini
[*.cs]
end_of_line = crlf

dotnet_diagnostic.AVIG0001.severity = error   # Tom rad efter avslutande klammer
dotnet_diagnostic.AVIG0002.severity = error   # Inga enbokstavsnamn
dotnet_diagnostic.AVIG0003.severity = error   # Tom rad före return
dotnet_diagnostic.AVIG0004.severity = error   # Tom rad före try
dotnet_diagnostic.AVIG0005.severity = error   # Tom rad före while
dotnet_diagnostic.AVIG0006.severity = error   # CRLF
dotnet_diagnostic.AVIG0007.severity = error   # Inget null som felsignal
dotnet_diagnostic.AVIG0008.severity = error   # Repository tar CancellationToken
dotnet_diagnostic.AVIG0009.severity = error   # XML-summary på controller/repository
dotnet_diagnostic.AVIG0010.severity = error   # Roll-/policy-namn som literal
dotnet_diagnostic.AVIG0011.severity = error   # <param>/<returns> utan <summary>
dotnet_diagnostic.AVIG0012.severity = error   # Controller-action utan [Authorize]
dotnet_diagnostic.AVIG0014.severity = error   # .Result / .Wait()
dotnet_diagnostic.AVIG0015.severity = error   # Tappad CancellationToken
dotnet_diagnostic.AVIG0020.severity = error   # DateTime.Now/Today
dotnet_diagnostic.AVIG0021.severity = error   # Loggmeddelande byggt, inte templat
dotnet_diagnostic.AVIG0022.severity = error   # Muterbar samling ut ur publik medlem
dotnet_diagnostic.AVIG0023.severity = error   # TODO utan referens
```

AVIG0008/0009/0010/0012 har inget att fästa på i en desktop-app men slås på ändå — de kostar
inget och följer med om koden växer.

Undantag som behövs:

- **S6640** (unsafe-kod) stängs av för `src/SnapShot/` — utan runtime-marshalling är pekare enda
  sättet att anropa Win32. `SnapShot.Core` har inget undantag. Motivering står i `.editorconfig`.
- **AVIG0022** behövde inget undantag: `Settings` har inga samlingar.
- **CS1591** i testprojektet: `GenerateDocumentationFile` är av där — testnamnen är dokumentationen.
- **CsWin32-genererad kod** räknas som genererad och analyseras inte; ingen åtgärd.
- Andra undantag tas med `#pragma warning disable <id>` och en motivering på raden ovanför, aldrig
  genom att sänka en regel globalt.

### Övriga filer

- **`.gitattributes`**: `*.cs text eol=crlf` — AVIG0006 smäller annars så fort ett verktyg skriver LF.
- **`nuget.config`**: nuget.org plus `G:\LocalNugetRepo`, med package source mapping där `Avig.*`
  får komma från båda (som i Medusa).
- **AOT-analys**: `PublishAot` i appen och `IsAotCompatible` i biblioteket slår på trimnings- och
  AOT-analyzers (`IL2xxx`, `IL3xxx`) redan vid `dotnet build`. Med warnings as errors blir varje
  AOT-osäker konstruktion ett byggfel, inte en överraskning vid publicering.

### Regler utan verktygsstöd (granskas manuellt)

Från `code-quality.md`: file-scoped namespaces, `using` utanför namespace, `_`-prefix på
`private readonly`-fält, Allman-klammer alltid, en sats per rad, early return (max två nivåer),
inga magic numbers/strings (gränsvärden som 4 px och 5 omförsök blir `const`), kommentarer
förklarar *varför*, inga `catch (Exception)` utan filter, inga förebyggande abstraktioner.

Konsekvens av AVIG0002 värd att nämna: inga `x`/`y`/`w`/`h` i geometrikoden — `left`, `top`,
`width`, `height`.

## Förutsättning i Windows 11

Windows 11 öppnar Snipping Tool på PrintScreen som standard. Det måste stängas av:

*Inställningar → Hjälpmedel → Tangentbord → "Använd Print Screen-knappen för att öppna skärmklippsverktyget" = Av*

(Registret: `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` = `0`.) Appen läser
värdet vid start och visar en notis om det är på.

## Flöde

```
PrintScreen nedtryckt
  └─ hook sväljer tangenten, PostMessage till huvudtråden
       └─ BitBlt av varje skärm till DIB (innan overlay visas)
            └─ visa ett overlay-fönster per skärm (frusen bild + mörk ton)
                 ├─ WM_LBUTTONDOWN → startpunkt, SetCapture
                 ├─ WM_MOUSEMOVE   → rita rektangel, ljus inuti, storlek i px
                 ├─ WM_LBUTTONUP   → beskär, PNG-koda, spara, kopiera sökväg, stäng
                 └─ Esc / WM_RBUTTONDOWN → avbryt
```

Detaljer:

- **Frys först, visa sedan.** Menyer och tooltips som var öppna kommer med.
- **Ett overlay per skärm** (`EnumDisplayMonitors`). Ett fönster över hela den virtuella skärmen
  beter sig dåligt med blandad skalning. I v1 hålls markeringen inom den skärm där man började.
- Overlay: `WS_POPUP`, `WS_EX_TOPMOST | WS_EX_TOOLWINDOW` (syns inte i aktivitetsfältet),
  hårkorsmarkör, ritning i minnes-DC och en `BitBlt` per `WM_PAINT`.
- Den dimmade bilden räknas ut en gång när overlayen öppnas, inte per musrörelse.
- Markering mindre än 4×4 px räknas som avbrott.
- Medan en overlay är öppen ignoreras nya PrintScreen-tryck.

## Spara fil

- Mapp: Windows egen skärmklippsmapp (känd mapp `FOLDERID_Screenshots`, den Win+PrintScreen
  sparar i), läst med `SHGetKnownFolderPath` vid varje klipp. Flyttar man mappen i Windows följer
  SnapShot med. Har Windows ingen sådan mapp används `Bilder\Screenshots`. Mappen skapas om den
  saknas. Hos Marcus pekar den på `G:\OneDrive\bilder\Screenshots`.
- Filnamn: `Screenshot_yyyy-MM-dd_HH-mm-ss.png` (lokal tid via `TimeProvider`); finns namnet
  redan läggs `_2`, `_3` … till.
- Skriv till en temporär fil i samma mapp och byt namn när den är klar, så att OneDrive inte
  synkar en halvskriven fil.
- PNG-kodaren: signatur, `IHDR` (färgtyp 2, RGB), `IDAT` med filtertyp *Sub* per rad genom
  `ZLibStream`, `IEND`. BGRA från DIB:en konverteras till RGB rad för rad med en buffert från
  `ArrayPool<byte>.Shared`.
- `Save` returnerar `Result<string>` med sökvägen.

## Klippbord

- Fullständig sökväg som ren text, utan citattecken:
  `G:\OneDrive\bilder\Screenshots\Screenshot_2026-10-03_16-45-12.png`.
- Klippbordet kan vara låst av ett annat program → upp till 5 försök med 50 ms mellanrum.
  Misslyckas det ändå returneras ett fel-`Result` och en notis visas (filen är sparad oavsett).

## Återkoppling

- Balloon-notis från systemfältsikonen: "Sparad och sökväg kopierad" + filnamn.
- Valfritt ljud (`MessageBeep`, av som standard).

## Systemfältsmeny

- Ta skärmklipp (samma som PrintScreen)
- Öppna mappen
- Starta med Windows (bock — `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SnapShot`)
- Avsluta

Ikonen läggs till igen när Explorer startas om (meddelandet `TaskbarCreated`).

## Inställningar

`%APPDATA%\SnapShot\settings.json`, skapas med standardvärden första gången och läses om vid
varje klipp. Läses och skrivs med `System.Text.Json`-källgenerering (`SettingsJsonContext`), ingen
reflektion.

```json
{
  "fileNamePattern": "Screenshot_{0:yyyy-MM-dd_HH-mm-ss}",
  "playSound": false,
  "showNotification": true
}
```

`outputFolder` finns inte i standardfilen: utan den används Windows skärmklippsmapp. Läggs den
till (`"outputFolder": "D:\\Klipp"`) vinner den över Windows mapp.

## Projektstruktur

```
SnapShot/
├─ plan.md
├─ SnapShot.slnx
├─ global.json                  SDK 10.0.400
├─ Directory.Build.props        analyzers, TreatWarningsAsErrors, Nullable
├─ .editorconfig                från Medusa, AVIG* = error
├─ .gitattributes               *.cs eol=crlf
├─ nuget.config
├─ src/
│  ├─ SnapShot.Core/            net10.0, IsAotCompatible — ren logik, ingen Win32
│  │  ├─ PngEncoder.cs          BGRA-buffert → PNG-ström
│  │  ├─ FileNameGenerator.cs   mönster + krockhantering, tar TimeProvider
│  │  ├─ SelectionRectangle.cs  normalisering (drag åt vänster/uppåt), minsta storlek
│  │  ├─ Settings.cs            record + standardvärden
│  │  ├─ SettingsJsonContext.cs JsonSerializerContext
│  │  ├─ SettingsStore.cs       läs/skriv settings.json → Result<Settings>
│  │  └─ ScreenshotWriter.cs    temp-fil + rename → Result<string>
│  └─ SnapShot/                 net10.0-windows, WinExe, PublishAot
│     ├─ NativeMethods.txt      lista över Win32-API:er för CsWin32
│     ├─ NativeMethods.json     { "allowMarshaling": false }
│     ├─ app.manifest           PerMonitorV2
│     ├─ app.ico
│     ├─ Program.cs             en instans (Mutex)
│     ├─ SnapShotApp.cs         dolt värdfönster, meddelandeloop, sparande på pooltråd
│     ├─ AppMessages.cs         egna WM_APP-meddelanden
│     ├─ TrayIcon.cs            Shell_NotifyIcon, meny, notiser
│     ├─ KeyboardHook.cs        WH_KEYBOARD_LL (PrintScreen, och Esc under markering)
│     ├─ ScreenCapture.cs       BitBlt per skärm → DIB + dimmad kopia
│     ├─ OverlayWindow.cs       fönsterklass, WndProc, ritning
│     ├─ ClipboardWriter.cs     CF_UNICODETEXT med omförsök → Result
│     └─ Autostart.cs           Run-nyckeln
└─ tests/
   └─ SnapShot.Core.Tests/      xUnit v3, net10.0
```

Paket utöver analyzers: `Microsoft.Windows.CsWin32` (källgenerator, `PrivateAssets="all"`),
`System.IO.Hashing` (CRC32), `Avig.Result`. Alla är AOT-kompatibla.

Uppdelningen i `Core` och app är inte en förebyggande abstraktion: den gör att den testbara
logiken kan testas på `net10.0` utan Win32, och `IsAotCompatible` på biblioteket fångar
AOT-problem direkt där. `SettingsStore` och `ScreenshotWriter` hamnade i `Core` av samma skäl —
de är ren fil-IO och testas där.

PNG-kodning, filskrivning och klippbord körs på en pooltråd: att koda ett stort område tar så
lång tid att meddelandeloopen — och därmed tangentbordshooken, som Windows tar bort om den är
långsam — annars skulle stå still.

## Bygga och köra

```powershell
dotnet build                                   # analyzers + AOT-analys, fel vid varning
dotnet test
dotnet run --project src/SnapShot
dotnet publish src/SnapShot -c Release -r win-x64   # Native AOT → bin/Release/net10.0-windows/win-x64/publish/SnapShot.exe
```

Installation: `./publish.ps1` kör testerna, publicerar med AOT, kopierar `SnapShot.exe` till
`G:\Apps\SnapShot`, lägger den i HKCU Run (autostart) och startar den. En körande instans från
målmappen stoppas först.

Native AOT kräver Visual Studio-arbetsbelastningen *Desktop development with C++* (länkaren).
Är miljövariabeln `NoDefaultCurrentDirectoryInExePath` satt (det gör vissa agent-skal) hittar
VS-skriptet inte `vswhere.exe` och länkningen faller med "'vswhere.exe' is not recognized" —
ta bort variabeln för publiceringskommandot.

## Status (2026-10-03)

Etapp 0–6 genomförda. `dotnet build` är grönt utan varningar, 27 enhetstester passerar och
AOT-exe:n är 2,7 MB och använder ~19 MB minne. Röktest av den publicerade exe:n (simulerad
PrintScreen och musdrag): två overlays på två skärmar, Esc stänger, drag ger en 300×200-PNG med
rätt innehåll och sökvägen på klippbordet, ett klick utan drag ger ingen fil, andra instansen
avslutas direkt. Ej verifierat automatiskt: om Skärmklippsverktyget också öppnas när Windows
inställning är på, Explorer-omstart och autostart efter omloggning.

## Etapper

0. **Repo-uppsättning** — `global.json`, `Directory.Build.props`, `.editorconfig`,
   `.gitattributes`, `nuget.config`, tomma projekt. Verifiera att reglerna faktiskt fyrar: lägg in
   en fil med `var`, LF-radslut, `DateTime.Now` och ett enbokstavsnamn, kontrollera att bygget
   faller på IDE0008, AVIG0006, AVIG0020 och AVIG0002, ta bort filen. Verifiera att
   `dotnet publish` med AOT ger en körbar exe.
1. **Skelett** — meddelandeloop, systemfältsikon, meny, Avsluta, en-instans-skydd. Publicera AOT
   och kör exe:n — AOT-problem ska hittas här, inte sist.
2. **Hook** — PrintScreen fångas och sväljs. Kontroll av Snipping Tool-inställningen.
3. **Overlay** — frusen bild, dimning, dra rektangel, Esc avbryter. En skärm först.
   *Beslutspunkt:* håller Win32-vägen, eller byt till WinForms utan AOT?
4. **Spara + klippbord** — PNG-kodare, temp-fil + rename, sökväg till klippbordet, notis.
5. **Flera skärmar och DPI** — ett overlay per skärm, testa 100 %/150 % blandat.
6. **Finputs** — inställningsfil, autostart, ljud.

Varje etapp är klar först när `dotnet build` är grönt utan undantag som saknar motivering.

## Test

Enhetstester (xUnit v3) i `SnapShot.Core.Tests`:

- `PngEncoder`: utdata börjar med PNG-signaturen, chunk-CRC:er stämmer, `IDAT` dekomprimerat med
  `ZLibStream` ger rätt pixlar för en känd 3×2-bild.
- `FileNameGenerator`: rätt namn med `FakeTimeProvider`, `_2`/`_3` vid krock.
- `SelectionRectangle`: drag åt alla fyra håll ger samma rektangel; under 4×4 px avvisas.
- `Settings`: saknad fil → standardvärden; rundtur genom `SettingsJsonContext`.

Manuellt, på den publicerade AOT-exe:n (inte `dotnet run`):

- [ ] PrintScreen öppnar overlay och Snipping Tool öppnas **inte**.
- [x] Dra och släpp → fil i mappen, öppnas korrekt, rätt område och full skärpa.
- [x] Ctrl+V i Claude Code klistrar in exakt sökväg; Claude kan läsa bilden.
- [ ] Esc och högerklick avbryter utan fil.
- [x] Litet klick (< 4 px) avbryter utan fil.
- [x] Två klipp inom samma sekund får olika filnamn.
- [ ] Skärm med 150 % skalning: rätt område, inte suddigt.
- [ ] Två skärmar: overlay på båda, klipp fungerar på båda.
- [x] Mappen saknas → skapas.
- [x] Appen startas två gånger → bara en instans.
- [ ] Explorer startas om → ikonen kommer tillbaka.
- [ ] Autostart på/av fungerar efter omstart.

## Risker

| Risk | Hantering |
|---|---|
| Win32-koden för overlay och ritning blir större än väntat | Beslutspunkt efter etapp 3; reserv är WinForms + ReadyToRun utan AOT. |
| AOT-problem upptäcks sent | AOT-analyzers i bygget som fel, och publicering redan från etapp 1. |
| Lågnivå-hooken tas bort av Windows om callbacken är långsam (> ~1 s) | Callbacken gör bara `PostMessage` och returnerar direkt. |
| Hooken fungerar inte mot fönster som körs som administratör | Känd begränsning; dokumenteras. |
| Blandad DPI ger förskjutna klipp | Ett overlay per skärm, PerMonitorV2, fysiska pixlar. |
| Klippbordet låst | Omförsök; filen sparas ändå. |
| OneDrive synkar halvskriven fil | Temp-fil + rename. |
| Analyzer-reglerna slåss mot genererad kod eller AOT-interop | Genererad kod analyseras inte; övriga fall `#pragma` med motivering. |
