#r "../../src/FlightDeck.Core/bin/Release/netstandard2.0/FlightDeck.Core.dll"

open System.IO

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let inputPath = Path.Combine(projectRoot, page)
    File.ReadAllText inputPath