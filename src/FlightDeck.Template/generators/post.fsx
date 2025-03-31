#r "nuget: Fornax.Core, 0.15.1"
#load "layout.fsx"

open Html

let generate' (ctx : SiteContents) (page: string) =
    let post =
        ctx.TryGetValues<Postloader.Post> ()
        |> Option.defaultValue Seq.empty
        |> Seq.tryFind (fun n -> n.file = page)

    match post with
    | Some post ->
        let siteInfo = ctx.TryGetValue<Globalloader.SiteInfo> ()
        let desc =
            siteInfo
            |> Option.map (fun si -> si.description)
            |> Option.defaultValue ""

        Layout.layout ctx post.title [
            section [Class "hero bg-primary text-primary-content"] [
                div [Class "hero-content text-center"] [
                    div [Class "max-w-md"] [
                        h1 [Class "text-4xl font-bold"] [!!desc]
                    ]
                ]
            ]
            div [Class "container mx-auto px-4"] [
                section [Class "py-8"] [
                    div [Class "max-w-3xl mx-auto"] [
                        Layout.postLayout false post
                    ]
                ]
            ]
        ]
    | None ->
        printfn "Warning: Post '%s' not found" page
        Layout.layout ctx "Post Not Found" [
            div [Class "container mx-auto px-4 py-8"] [
                div [Class "card bg-warning text-warning-content max-w-md mx-auto"] [
                    div [Class "card-body"] [
                        h2 [Class "card-title"] [!!"Post Not Found"]
                        p [] [!!(sprintf "The post '%s' could not be found." page)]
                        a [Class "btn"; Href "/"] [!!"Return Home"]
                    ]
                ]
            ]
        ]

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    generate' ctx page
    |> Layout.render ctx