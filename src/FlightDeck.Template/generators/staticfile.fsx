#r "nuget: Fornax.Core, 0.15.1"

open System.IO

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let inputPath = Path.Combine(projectRoot, page)
    File.ReadAllBytes inputPath  