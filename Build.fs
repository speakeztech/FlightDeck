
open Fake.Core
open Fake.DotNet
open Fake.Tools
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Api

open Helpers

initializeContext()

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let project = "FlightDeck"
let summary = "FlightDeck is a static site generator using type safe F# DSL to define page layouts"
let nugetOrg = "https://api.nuget.org/v3/index.json"
let gitOwner = "speakez-llc"
let gitHome = "https://github.com/" + gitOwner
let gitName = "FlightDeck"
let gitRaw = Environment.environVarOrDefault "gitRaw" ("https://raw.github.com/" + gitOwner)

// --------------------------------------------------------------------------------------
// Build variables
// --------------------------------------------------------------------------------------

System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let packageDir = __SOURCE_DIRECTORY__ </> "out"
let buildDir = __SOURCE_DIRECTORY__ </> "temp"


// --------------------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------------------
let isNullOrWhiteSpace = System.String.IsNullOrWhiteSpace

let runTool cmd args workingDir =
    let arguments = args |> String.split ' ' |> Arguments.OfArgs
    let r =
        Command.RawCommand (cmd, arguments)
        |> CreateProcess.fromCommand
        |> CreateProcess.withWorkingDirectory workingDir
        |> Proc.run
    if r.ExitCode <> 0 then
        failwithf "Error while running '%s' with args: %s" cmd args

let getBuildParam = Environment.environVar

let DoNothing = ignore

// --------------------------------------------------------------------------------------
// Build Targets
// --------------------------------------------------------------------------------------

Target.create "Clean" (fun _ ->
    Shell.cleanDirs [buildDir; packageDir]
)

Target.create "Restore" (fun _ ->
    DotNet.restore id "FlightDeck.sln"
)

Target.create "Build" (fun _ ->
    DotNet.build id "FlightDeck.sln"
)

Target.create "Publish" (fun _ ->
    DotNet.publish (fun p -> {p with OutputPath = Some buildDir}) "src/FlightDeck"
)

Target.create "Test" (fun _ ->
    runTool "dotnet" @"run --project .\test\FlightDeck.Core.UnitTests\FlightDeck.Core.UnitTests.fsproj" "."
)

Target.create "TestTemplate" (fun _ ->
    let templateDir = __SOURCE_DIRECTORY__ </> "src/FlightDeck.Template/"
    let coreDllSource = buildDir </> "FlightDeck.Core.dll"
    let coreDllDest = templateDir </> "_lib"  </> "FlightDeck.Core.dll"

    try
        System.IO.File.Copy(coreDllSource, coreDllDest, true)

        let newlyBuiltFlightDeck = buildDir </> "FlightDeck.dll"

        printfn "templateDir: %s" templateDir

        runTool "dotnet" (sprintf "%s watch" newlyBuiltFlightDeck) templateDir

    finally
        File.delete coreDllDest
)

// --------------------------------------------------------------------------------------
// Release Targets
// --------------------------------------------------------------------------------------

Target.create "Pack" (fun _ ->
    [
        "src/FlightDeck"
        "src/FlightDeck.Core"
    ]
    |> List.iter(
        DotNet.pack (fun p ->
            { p with
                OutputPath = Some packageDir
                Configuration = DotNet.BuildConfiguration.Release
            }))
)

Target.create "Push" (fun _ ->
    let key = 
        match System.Environment.GetEnvironmentVariable("NUGET_KEY") with
        | null -> 
            printfn "ERROR: Cannot retrieve NuGet key from environment"
            failwith "NuGet API key is required for pushing packages"
        | s when s.Trim() = "" -> 
            printfn "ERROR: NuGet key is an empty string"
            failwith "NuGet API key is required for pushing packages"
        | s -> s
    
    try
        DotNet.nugetPush (fun p ->
            { p with
                PushParams = { p.PushParams with
                                ApiKey = Some key
                                Source = Some nugetOrg } }
        ) $"{packageDir}/*.nupkg"
    with
    | ex -> 
        printfn "NuGet push failed: %s" ex.Message
        reraise()
)

// --------------------------------------------------------------------------------------
// Build order
// --------------------------------------------------------------------------------------
Target.create "Default" DoNothing
Target.create "Release" DoNothing

let dependencies = [
    "Clean"
      ==> "Restore"
      ==> "Build"
      ==> "Publish"
      ==> "Test"
      ==> "Default"

    "Restore"
      ==> "Build"
      ==> "Publish"
      ==> "TestTemplate"

    "Default"
      ==> "Pack"
      ==> "Push"
      ==> "Release"
]

[<EntryPoint>]
let main args = 
    runOrDefault "Pack" args