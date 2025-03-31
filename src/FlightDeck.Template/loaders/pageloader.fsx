#r "nuget: Fornax.Core, 0.15.1"
#r "nuget: Markdig, 0.32.0"

open System.IO
open Markdig

// Define the Page type
type Page = {
    title: string
    link: string
    content: string
    file: string
}

let markdownPipeline =
    MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseGridTables()
        .Build()

let getConfig (fileContent : string) =
    let fileContent = fileContent.Split '\n'
    let fileContent = fileContent |> Array.skip 1 // First line must be ---
    let indexOfSeperator = fileContent |> Array.findIndex (fun s -> s.StartsWith "---")
    let splitKey (line: string) =
        let seperatorIndex = line.IndexOf(':')
        if seperatorIndex > 0 then
            let key = line.[.. seperatorIndex - 1].Trim().ToLower()
            let value = line.[seperatorIndex + 1 ..].Trim()
            Some(key, value)
        else
            None
    fileContent
    |> Array.splitAt indexOfSeperator
    |> fst
    |> Seq.choose splitKey
    |> Map.ofSeq

let getContent (fileContent : string) =
    let fileContent = fileContent.Split '\n'
    let fileContent = fileContent |> Array.skip 1 // First line must be ---
    let indexOfSeperator = fileContent |> Array.findIndex (fun s -> s.StartsWith "---")
    fileContent 
    |> Array.skip (indexOfSeperator + 1) 
    |> String.concat "\n"
    |> fun content -> Markdig.Markdown.ToHtml(content, markdownPipeline)

let trimString (str : string) =
    str.Trim().TrimEnd('"').TrimStart('"')

let isValidPage (filePath: string) =
    let ext = Path.GetExtension(filePath)
    let dir = Path.GetDirectoryName(filePath)
    ext = ".md" && 
    not (dir.Contains("_public")) && 
    not (Path.GetFileName(filePath).StartsWith("_")) &&
    not (dir.Contains("posts")) &&
    not (filePath.Contains("\\_public\\")) && 
    not (filePath.StartsWith("_public"))   

let loadFile (rootDir: string) (n: string) =
    try
        let text = File.ReadAllText n
        let config = getConfig text
        let content = getContent text

        let fileName = Path.GetFileNameWithoutExtension(n)
        let title = 
            config 
            |> Map.tryFind "title" 
            |> Option.map trimString 
            |> Option.defaultValue (if fileName = "index" then "Home" else fileName)

        let link = 
            if fileName = "index" then "/"
            else "/" + fileName + ".html"

        // Store the full path relative to project root
        let chopLength =
            if rootDir.EndsWith(Path.DirectorySeparatorChar) then rootDir.Length
            else rootDir.Length + 1

        let file = n.Substring(chopLength).Replace("\\", "/")

        Some {
            title = title
            link = link
            content = content
            file = file  // Store full relative path
        }
    with ex ->
        printfn "Error processing %s: %s" n ex.Message
        None

let loader (projectRoot: string) (siteContent: SiteContents) =
    // Add default navigation pages
    siteContent.Add({title = "Home"; link = "/"; content = ""; file = "index.md"})
    siteContent.Add({title = "About"; link = "/about.html"; content = ""; file = "about.md"})
    siteContent.Add({title = "Contact"; link = "/contact.html"; content = ""; file = "contact.md"})
    
    // Load pages from files in the pages directory
    let pagesPath = Path.Combine(projectRoot, "pages")
    if Directory.Exists(pagesPath) then
        let pageFiles = Directory.GetFiles(pagesPath, "*.md", SearchOption.AllDirectories)
        pageFiles
        |> Array.filter (fun p -> not (Path.GetFileName(p).StartsWith("_")))
        |> Array.choose (loadFile projectRoot)
        |> Array.iter siteContent.Add
    
    siteContent