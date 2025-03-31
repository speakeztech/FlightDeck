#r "nuget: Fornax.Core, 0.15.1"
#load "layout.fsx"

open Html
open System.IO

// Define Page type for static pages
type Page = {
    title: string
    link: string
    content: string
    file: string
}

let generate' (ctx : SiteContents) (page: string) =
    // Get all available pages
    let allPages = 
        ctx.TryGetValues<Page>() 
        |> Option.defaultValue Seq.empty 
        |> Seq.toList
    
    // Try to find the page by comparing filenames
    let pageOption = 
        allPages 
        |> Seq.tryFind (fun p -> 
            let pageFile = Path.GetFileName(page)
            let pFile = Path.GetFileName(p.file)
            pFile = pageFile || p.file = page
        )
    
    match pageOption with
    | Some pageData ->
        // Use Layout.layout which includes the navigation bar
        Layout.layout ctx pageData.title [
            section [Class "hero bg-primary text-primary-content"] [
                div [Class "hero-content text-center"] [
                    div [Class "max-w-md"] [
                        let siteInfo = ctx.TryGetValue<Globalloader.SiteInfo> ()
                        let desc =
                            siteInfo
                            |> Option.map (fun si -> si.description)
                            |> Option.defaultValue ""
                        h1 [Class "text-4xl font-bold text-white"] [!!desc]
                    ]
                ]
            ]
            div [Class "container mx-auto px-4"] [
                section [Class "py-8"] [
                    div [Class "max-w-3xl mx-auto"] [
                        div [Class "card bg-base-100 shadow-xl"] [
                            div [Class "card-body"] [
                                div [Class "prose max-w-none"] [
                                    !!pageData.content
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    | None ->
        // Page not found - create a simple error page
        Layout.layout ctx "Page Not Found" [
            div [Class "container mx-auto px-4 py-8"] [
                div [Class "card bg-warning text-warning-content max-w-md mx-auto"] [
                    div [Class "card-body"] [
                        h2 [Class "card-title"] [!!"Page Not Found"]
                        p [] [!!(sprintf "The page '%s' could not be found." page)]
                        a [Class "btn"; Href "/"] [!!"Return Home"]
                    ]
                ]
            ]
        ]

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    try
        let rendered = generate' ctx page
        Layout.render ctx rendered
    with ex ->
        printfn "Error in page generator: %s" ex.Message
        
        // Create a simple error page
        let errorPage = Layout.layout ctx "Error" [
            div [Class "container mx-auto px-4 py-8"] [
                div [Class "card bg-error text-error-content max-w-md mx-auto"] [
                    div [Class "card-body"] [
                        h2 [Class "card-title"] [!!"Error Generating Page"]
                        p [] [!!"There was an error generating this page. Please check the console for details."]
                    ]
                ]
            ]
        ]
        
        Layout.render ctx errorPage