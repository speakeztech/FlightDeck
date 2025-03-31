module FlightDeck

open System
open System.IO
open System.Diagnostics
open Argu
open Suave
open Suave.Filters
open Suave.Operators
open LibGit2Sharp
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open System.Reflection
open Logger

type FlightDeckExiter () =
    interface IExiter with
        member x.Name = "FlightDeck exiter"
        member x.Exit (msg, errorCode) =
            if errorCode = ErrorCode.HelpText then
                printf $"%s{msg}"
                exit 0
            else
                errorfn $"Error with code %A{errorCode} received - exiting."
                printf $"%s{msg}"
                exit 1

type [<CliPrefix(CliPrefix.DoubleDash)>] WatchOptions =
    | Port of int
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Port _ -> "Specify a custom port (default: 8080)"

// Define separate types without alternate commands
type [<CliPrefix(CliPrefix.DoubleDash)>] NewOptions =
    | Template of string
    | Output of string
    | LightTheme of string
    | DarkTheme of string
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Template _ -> "Specify a template from an HTTPS git repo or local folder"
            | Output _ -> "Specify an output folder"
            | LightTheme _ -> "Light theme (default: light)"
            | DarkTheme _ -> "Dark theme (default: dark)"

type [<CliPrefix(CliPrefix.None)>] Arguments =
    | New of ParseResults<NewOptions>
    | Build
    | Watch of ParseResults<WatchOptions>
    | Version
    | Clean
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | New _ -> "Create new web site"
            | Build -> "Build web site"
            | Watch _ -> "Start watch mode rebuilding "
            | Version -> "Print version"
            | Clean -> "Clean output and temp files"

let lightTheme = "light"
let darkTheme = "dark"

// Event to signal content changes for live reload
let signalContentChanged = Event<Choice<unit, Error>>()

let createFileWatcher dir handler =
    let fileSystemWatcher = new FileSystemWatcher()
    fileSystemWatcher.Path <- dir
    fileSystemWatcher.EnableRaisingEvents <- true
    fileSystemWatcher.IncludeSubdirectories <- true
    fileSystemWatcher.NotifyFilter <- NotifyFilters.DirectoryName ||| NotifyFilters.LastWrite ||| NotifyFilters.FileName
    fileSystemWatcher.Created.Add handler
    fileSystemWatcher.Changed.Add handler
    fileSystemWatcher.Deleted.Add handler

    // Handler to trigger websocket refresh
    let contentChangedHandler _ =
        signalContentChanged.Trigger(Choice<unit,Error>.Choice1Of2 ())
        GeneratorEvaluator.removeItemFromGeneratorCache()

    signalContentChanged.Trigger(Choice<unit,Error>.Choice1Of2 ())
    fileSystemWatcher.Created.Add contentChangedHandler
    fileSystemWatcher.Changed.Add contentChangedHandler
    fileSystemWatcher.Deleted.Add contentChangedHandler
    fileSystemWatcher

// WebSocket handler for live reload
let ws (webSocket : WebSocket) (context: HttpContext) =
    informationfn "Opening WebSocket - new handShake"
    socket {
        try
            while true do
                do! Async.AwaitEvent signalContentChanged.Publish
                informationfn "Signalling content changed"
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
        finally
            informationfn "Disconnecting WebSocket"
    }

let getWebServerConfig port =
    match port with
    | Some port ->
        { defaultConfig with
            bindings = [ HttpBinding.create Protocol.HTTP Net.IPAddress.Loopback port ] }
    | None -> defaultConfig

let getOutputDirectory (output : option<string>) (cwd : string) = 
    match output with
    | Some output -> output
    | None -> cwd

// File/directory utilities
let normalizeFiles directory =
    Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
    |> Seq.iter (fun path -> File.SetAttributes(path, FileAttributes.Normal))
    directory

let deleteDirectory directory =
    Directory.Delete(directory, true)

let deleteGit (gitDirectory : string) =
    if Directory.Exists gitDirectory then 
        gitDirectory |> normalizeFiles |> deleteDirectory
        
let copyDirectories (input : string) (output : string) = 
    // Copy directories
    Directory.GetDirectories(input, "*", SearchOption.AllDirectories)
    |> Seq.iter (fun p -> Directory.CreateDirectory(p.Replace(input, output)) |> ignore)

    // Copy files
    Directory.GetFiles(input, "*.*", SearchOption.AllDirectories)
    |> Seq.iter (fun p -> File.Copy(p, p.Replace(input, output)))

// Initialize npm project with Tailwind/DaisyUI if requested
let initializeModernFeatures (outputDirectory: string) (lightTheme: string) (darkTheme: string)  =
    // Create directories
    let styleDir = Path.Combine(outputDirectory, "style")
    if not (Directory.Exists(styleDir)) then
        Directory.CreateDirectory(styleDir) |> ignore
        
    let loadersDir = Path.Combine(outputDirectory, "loaders")
    if Directory.Exists(loadersDir) then
        let globalLoaderPath = Path.Combine(loadersDir, "globalloader.fsx")
        if File.Exists(globalLoaderPath) then
            let content = File.ReadAllText(globalLoaderPath)
            let updatedContent = 
                if content.Contains("type SiteInfo =") then
                    content.Replace(
                        "type SiteInfo =",
                        """type SiteInfo = {
                        title: string
                        description: string
                        postPageSize: int
                        lightTheme: string
                        darkTheme: string
                    }""")
                else
                    content
                    
            let updatedContent2 = 
                if updatedContent.Contains("let siteInfo =") then
                    updatedContent.Replace(
                        "let siteInfo =",
                        $"""let siteInfo =
        {{ title = "FlightDeck Site";
          description = "A modern static site built with FlightDeck"
          postPageSize = 5
          lightTheme = "%s{lightTheme}"
          darkTheme = "%s{darkTheme}" }}""")
                else
                    updatedContent
                    
            File.WriteAllText(globalLoaderPath, updatedContent2)    
    // Create package.json
    let packageJsonPath = Path.Combine(outputDirectory, "package.json")
    let packageJsonContent = 
        """{
  "name": "flightdeck-site",
  "version": "1.0.0",
  "description": "Modern static site built with FlightDeck",
  "private": true,
  "scripts": {
    "build:css": "postcss style/style.css -o _public/style/style.css",
    "watch:css": "postcss style/style.css -o _public/style/style.css --watch"
  },
  "devDependencies": {
    "@tailwindcss/typography": "^0.5.10",
    "autoprefixer": "^10.4.14",
    "postcss": "^8.4.23",
    "postcss-cli": "^10.1.0",
    "tailwindcss": "^3.3.2"
  },
  "dependencies": {
    "daisyui": "^3.5.0"
  }
}"""
    File.WriteAllText(packageJsonPath, packageJsonContent)
    
    // Create postcss.config.js
    let postcssConfigPath = Path.Combine(outputDirectory, "postcss.config.js")
    let postcssConfigContent = 
        """module.exports = {
  plugins: {
    tailwindcss: {},
    autoprefixer: {},
  },
}"""
    File.WriteAllText(postcssConfigPath, postcssConfigContent)
    
    // Create tailwind.config.js
    let tailwindConfigPath = Path.Combine(outputDirectory, "tailwind.config.js")
    let tailwindConfigContent = 
        """/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./_public/**/*.html",
    "./style/**/*.css",
  ],
  theme: {
    extend: {}
  },
  plugins: [
    require("daisyui"),
    require("@tailwindcss/typography"),
  ],
  daisyui: {
    themes: true,
    themeRoot: ":root",
  },
}"""
    File.WriteAllText(tailwindConfigPath, tailwindConfigContent)
    
    // Create base CSS with Tailwind
    let styleCssPath = Path.Combine(styleDir, "style.css")
    let styleCssContent = 
        """@tailwind base;
@tailwind components;
@tailwind utilities;

:root {
  --transition-duration: 0.4s;
}

html {
  transition: background-color var(--transition-duration) ease-in-out;
}"""
    File.WriteAllText(styleCssPath, styleCssContent)
    
    // Create tailwind.fsx generator
    let generatorsDir = Path.Combine(outputDirectory, "generators")
    if Directory.Exists(generatorsDir) then
        let tailwindFsxPath = Path.Combine(generatorsDir, "tailwind.fsx")
        let tailwindFsxContent = 
            """#r "nuget: FlightDeck.Core, 0.20.0"

open System
open System.IO
open System.Diagnostics

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let outputPath = Path.Combine(projectRoot, "_public", Path.GetDirectoryName(page), Path.GetFileName(page))
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)) |> ignore
    
    try
        let psi = new ProcessStartInfo()
        if Environment.OSVersion.Platform = PlatformID.Win32NT then
            psi.FileName <- "cmd"
            psi.Arguments <- sprintf "/c npx postcss %s -o %s" page outputPath
        else
            psi.FileName <- "npx"
            psi.Arguments <- sprintf "postcss %s -o %s" page outputPath
            
        psi.WorkingDirectory <- projectRoot
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        
        use proc = Process.Start(psi)
        
        let output = proc.StandardOutput.ReadToEnd()
        let error = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        
        if proc.ExitCode <> 0 then
            printfn "PostCSS failed: %s" error
            File.ReadAllBytes page // Fallback
        else
            if File.Exists(outputPath) then
                File.ReadAllBytes outputPath
            else
                File.ReadAllBytes page // Fallback
    with e ->
        printfn "Error: %s" e.Message
        File.ReadAllBytes page // Fallback"""
        File.WriteAllText(tailwindFsxPath, tailwindFsxContent)
// Template handling
let handleTemplate (template: option<string>) (outputDirectory: string) (lightTheme: string) (darkTheme: string): unit = 
    // Clone or copy template
    match template with
    | Some template ->
        let uriTest, _ = Uri.TryCreate(template, UriKind.Absolute)
        match uriTest with
        | true -> 
            informationfn $"Cloning template from %s{template}"
            Repository.Clone(template, outputDirectory) |> ignore
            Path.Combine(outputDirectory, ".git") |> deleteGit
        | false -> 
            informationfn $"Copying template from %s{template}"
            copyDirectories template outputDirectory
            Path.Combine(outputDirectory, ".git") |> deleteGit
    | None ->
        // Default template
        let path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blogTemplate")
        informationfn "Creating a new site using the default template"
        copyDirectories path outputDirectory
    
    // Setup Tailwind/DaisyUI and update theme settings
    initializeModernFeatures outputDirectory lightTheme darkTheme
    okfn $"Site created with light theme: %s{lightTheme}, dark theme: %s{darkTheme}"

    // Install npm dependencies
    let packageJsonPath = Path.Combine(outputDirectory, "package.json")
    if File.Exists(packageJsonPath) then
        try
            informationfn "Installing npm dependencies..."
            let psi = ProcessStartInfo()
            if Environment.OSVersion.Platform = PlatformID.Win32NT then
                psi.FileName <- "cmd"
                psi.Arguments <- "/c npm install"
            else
                psi.FileName <- "npm"
                psi.Arguments <- "install"
            psi.WorkingDirectory <- outputDirectory
            psi.UseShellExecute <- false
            
            let proc = Process.Start(psi)
            proc.WaitForExit()
            
            if proc.ExitCode = 0 then
                okfn "npm dependencies installed successfully!"
            else
                informationfn "npm installation failed. You may need to run 'npm install' manually."
        with ex ->
            informationfn $"Failed to run npm install: %s{ex.Message}"

let router basePath =
    choose [
        path "/" >=> Redirection.redirect "/index.html"
        (Files.browse (Path.Combine(basePath, "_public")))
        path "/websocket" >=> handShake ws
    ]

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<Arguments>(programName = "FlightDeck", errorHandler=FlightDeckExiter())
    let results = parser.ParseCommandLine(inputs = argv).GetAllResults()

    if List.isEmpty results then
        errorfn "No arguments provided. Try 'FlightDeck help' for details."
        printfn "%s" <| parser.PrintUsage()
        1
    elif List.length results > 1 then
        errorfn "More than one command provided. Please provide only a single command."
        printfn "%s" <| parser.PrintUsage()
        1
    else
        let result = List.tryHead results
        let cwd = Directory.GetCurrentDirectory()

        match result with
        | Some (New newOptions) ->
            let outputDirectory = getOutputDirectory (newOptions.TryPostProcessResult(<@ Output @>, string)) cwd
            let lightTheme = newOptions.TryPostProcessResult(<@ LightTheme @>, string) |> Option.defaultValue "light"
            let darkTheme = newOptions.TryPostProcessResult(<@ DarkTheme @>, string) |> Option.defaultValue "dark"
            
            handleTemplate 
                (newOptions.TryPostProcessResult(<@ Template @>, string)) 
                outputDirectory 
                lightTheme 
                darkTheme
            
            0
            
        | Some Build ->
            try
                let sc = SiteContents()
                do generateFolder sc cwd false
                0
            with
            | FlightDeckGeneratorException message ->
                message |> stringFormatter |> errorfn
                1
            | exn ->
                errorfn $"An unexpected error occurred: {exn}"
                1
                
        | Some (Watch watchOptions) ->
            let mutable lastAccessed = Map.empty<string, DateTime>
            let waitingForChangesMessage = "Generated site with errors. Waiting for changes..."
            let sc = SiteContents()

            let guardedGenerate() =
                try
                    do generateFolder sc cwd true
                with
                | FlightDeckGeneratorException message ->
                    message |> stringFormatter |> errorfn 
                    waitingForChangesMessage |> stringFormatter |> informationfn
                | exn ->
                    errorfn $"An unexpected error occurred: {exn}"
                    exit 1

            guardedGenerate()

            use watcher = createFileWatcher cwd (fun e ->
                let pathDirectories = 
                    Path.GetRelativePath(cwd, e.FullPath)
                        .Split(Path.DirectorySeparatorChar)
                
                let shouldIgnore =
                    pathDirectories
                    |> Array.exists (fun fragment ->
                        fragment = "_public" ||     
                        fragment = ".sass-cache" ||
                        fragment = "node_modules" ||
                        fragment = ".git" ||           
                        fragment = ".ionide")
                
                if not shouldIgnore then
                    let lastTimeWrite = File.GetLastWriteTime(e.FullPath)
                    match lastAccessed.TryFind e.FullPath with
                    | Some lt when Math.Abs((lt - lastTimeWrite).Seconds) < 1 -> ()
                    | _ ->
                        informationfn "[%s] Changes detected: %s" (DateTime.Now.ToString("HH:mm:ss")) e.FullPath
                        lastAccessed <- lastAccessed.Add(e.FullPath, lastTimeWrite)
                        guardedGenerate())

            let webServerConfig = getWebServerConfig (watchOptions.TryPostProcessResult(<@ Port @>, uint16))
            startWebServerAsync webServerConfig (router cwd) |> snd |> Async.Start
            okfn "[%s] Watch mode started." (DateTime.Now.ToString("HH:mm:ss"))
            informationfn "Press any key to exit."
            Console.ReadKey() |> ignore
            informationfn "Exiting..."
            0
            
        | Some Version -> 
            let assy = Assembly.GetExecutingAssembly()
            let v = assy.GetCustomAttributes<AssemblyVersionAttribute>() |> Seq.head
            printfn $"%s{v.Version}"
            0
            
        | Some Clean ->
            let publ = Path.Combine(cwd, "_public")
            let sassCache = Path.Combine(cwd, ".sass-cache")
            let nodeModules = Path.Combine(cwd, "node_modules")
            let deleter folder = 
                if Directory.Exists(folder) then 
                    Directory.Delete(folder, true)
            
            try
                [publ; sassCache; nodeModules] |> List.iter deleter
                okfn "Cleaned output and temporary directories."
                0
            with ex ->
                errorfn $"Error cleaning directories: {ex}"
                1
                
        | None ->
            errorfn "Unknown argument"
            printfn "%s" <| parser.PrintUsage()
            1