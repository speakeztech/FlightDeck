#r "nuget: Fornax.Core, 0.15.1"

open Config
open System.IO

// Helper functions to filter files
let isInNodeModules (path: string) =
    let normalizedPath = path.Replace('\\', '/').ToLower()
    normalizedPath.Contains("/node_modules/") || 
    normalizedPath.Contains("\\node_modules\\") ||
    normalizedPath.Contains("node_modules")

let isInPublicFolder (path: string) =
    path.Contains "_public" ||
    path.Contains("/_public/") ||
    path.Contains("\\_public\\") ||
    path.StartsWith("_public")

let postPredicate (projectRoot: string, page: string) =
    // Skip files in node_modules or _public
    if isInNodeModules(page) || isInPublicFolder(page) then
        false
    else
        let fileName = Path.Combine(projectRoot,page)
        let ext = Path.GetExtension page
        if ext = ".md" then
            let ctn = File.ReadAllText fileName
            page.Contains("_public") |> not
            && ctn.Contains("layout: post")
        else
            false

let pagePredicate (projectRoot: string, page: string) =
    // Skip files in node_modules or _public
    if isInNodeModules(page) || isInPublicFolder(page) then
        false
    else
        let fileName = Path.Combine(projectRoot,page)
        let ext = Path.GetExtension page
        if ext = ".md" then
            let ctn = File.ReadAllText fileName
            page.Contains("_public") |> not
            && ctn.Contains("layout: page")
        else
            false

let staticPredicate (projectRoot: string, page: string) =
    // Skip files in node_modules or _public
    if isInNodeModules(page) || isInPublicFolder(page) then
        false
    else
        let ext = Path.GetExtension page
        let fileShouldBeExcluded =
            ext = ".fsx" ||
            ext = ".md"  ||
            page.Contains "_public" ||
            page.Contains "_bin" ||
            page.Contains "_lib" ||
            page.Contains "_data" ||
            page.Contains "_settings" ||
            page.Contains "_config.yml" ||
            page.Contains ".sass-cache" ||
            page.Contains ".git" ||
            page.Contains ".ionide" ||
            page.Contains "package.json" ||
            page.Contains "package-lock.json" ||
            page.Contains "tailwind.config.js" ||
            page.Contains "postcss.config.js"
        fileShouldBeExcluded |> not

let tailwindPredicate (projectRoot: string, page: string) =
    // Only process main CSS files
    if isInNodeModules(page) || isInPublicFolder(page) then
        false
    else
        let ext = Path.GetExtension page
        ext = ".css" && page.Contains("style/style.css")

// Function to output page files with proper names
let pageOutput (page: string) =
    let fileName = Path.GetFileNameWithoutExtension(page)
    if fileName.ToLower() = "index" then
        "index.html"
    else
        fileName + ".html"

let config = {
    Generators = [
        {Script = "less.fsx"; Trigger = OnFileExt ".less"; OutputFile = ChangeExtension "css" }
        {Script = "sass.fsx"; Trigger = OnFileExt ".scss"; OutputFile = ChangeExtension "css" }
        {Script = "post.fsx"; Trigger = OnFilePredicate postPredicate; OutputFile = ChangeExtension "html" }
        {Script = "page.fsx"; Trigger = OnFilePredicate pagePredicate; OutputFile = Custom pageOutput }
        {Script = "staticfile.fsx"; Trigger = OnFilePredicate staticPredicate; OutputFile = SameFileName }
        {Script = "index.fsx"; Trigger = Once; OutputFile = MultipleFiles id }
        {Script = "about.fsx"; Trigger = Once; OutputFile = NewFileName "about.html" }
        {Script = "contact.fsx"; Trigger = Once; OutputFile = NewFileName "contact.html" }
        {Script = "tailwind.fsx"; Trigger = OnFilePredicate tailwindPredicate; OutputFile = SameFileName }
    ]
}