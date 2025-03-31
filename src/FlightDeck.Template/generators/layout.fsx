#r "nuget: Fornax.Core, 0.15.1"
#if !FlightDeck
#load "../loaders/postloader.fsx"
#load "../loaders/pageloader.fsx"
#load "../loaders/globalloader.fsx"
#endif

open Html

let injectWebsocketCode (webpage:string) =
    let websocketScript =
        """
        <script type="text/javascript">
          var wsUri = "ws://localhost:8080/websocket";
      function init()
      {
        websocket = new WebSocket(wsUri);
        websocket.onclose = function(evt) { onClose(evt) };
      }
      function onClose(evt)
      {
        console.log('closing');
        websocket.close();
        document.location.reload();
      }
      window.addEventListener("load", init, false);
      </script>
        """
    let head = "<head>"
    let index = webpage.IndexOf head
    webpage.Insert ( (index + head.Length + 1),websocketScript)

// Navigation item type for the menu
type NavItem = {
    title: string
    link: string
    icon: string
}

// Standard navigation across all pages
let getStandardNavigation () =
    [
        { NavItem.title = "Home"; link = "/"; icon = "fa-solid fa-home text-xl" }
        { NavItem.title = "Blog"; link = "/posts/index.html"; icon = "fa-solid fa-newspaper text-xl" }
        { NavItem.title = "About"; link = "/about.html"; icon = "fa-solid fa-user text-xl" }
        { NavItem.title = "Contact"; link = "/contact.html"; icon = "fa-solid fa-envelope text-xl" }
    ]

// Creates a consistent navigation bar for all pages
let createNavBar (active: string) (ctx : SiteContents) =
    let pages = ctx.TryGetValues<Pageloader.Page> () |> Option.defaultValue Seq.empty
    let siteInfo = ctx.TryGetValue<Globalloader.SiteInfo> ()
    let ttl, darkTheme, lightTheme =
      siteInfo
      |> Option.map (fun si -> si.title, si.darkTheme, si.lightTheme)
      |> Option.defaultValue ("", "dark", "light")
    
    let menuEntries =
        pages
        |> Seq.map (fun p ->
            let cls = if p.title = active then "active" else ""
            li [] [
                a [Class cls; Href p.link] [
                    !! p.title
                ]
            ])
        |> Seq.toList

    nav [Class "navbar bg-base-100"] [ 
        div [Class "container mx-auto flex justify-between items-center"] [
            div [Class "navbar-start"] [
                a [Class "btn btn-ghost text-xl"; Href "/"] [
                    img [Src "/images/logo.png"; Alt "Logo"; Class "h-8 mr-2"]
                    !! ttl
                ]
            ]
            div [Class "navbar-center hidden lg:flex"] [
                ul [Class "menu menu-horizontal px-1"] menuEntries
            ]
            div [Class "navbar-end"] [
                label [Class "swap swap-rotate mr-4"] [
                    input [Type "checkbox"; Class "theme-controller"; Value lightTheme]
                    i [Class "swap-on fa-solid fa-moon text-xl"] []
                    i [Class "swap-off fa-solid fa-sun text-xl"] []
                ]
                div [Class "dropdown dropdown-end lg:hidden"] [
                    label [TabIndex 0; Class "btn btn-ghost"] [
                        i [Class "fa-solid fa-bars text-xl"] []
                    ]
                    ul [TabIndex 0; Class "dropdown-content menu p-2 shadow bg-base-100 rounded-box w-52"] menuEntries
                ]
            ]
        ]
    ]

let layout (ctx : SiteContents) active bodyCnt =
    let siteInfo = ctx.TryGetValue<Globalloader.SiteInfo> ()
    let ttl, darkTheme, lightTheme =
      siteInfo
      |> Option.map (fun si -> si.title, si.darkTheme, si.lightTheme)
      |> Option.defaultValue ("", "dark", "light")

    let navBar = createNavBar active ctx

    html [HtmlProperties.Custom ("data-theme", darkTheme)] [
        head [] [
            meta [CharSet "utf-8"]
            meta [Name "viewport"; Content "width=device-width, initial-scale=1"]
            title [] [!! ttl]
            link [Rel "icon"; Type "image/png"; Sizes "32x32"; Href "/images/favicon.png"]
            link [Rel "stylesheet"; Href "https://fonts.googleapis.com/css?family=Open+Sans"]
            link [Rel "stylesheet"; Type "text/css"; Href "/style/style.css"]
            script [Src "https://kit.fontawesome.com/3e50397676.js"; CrossOrigin "anonymous"] []
            script [] [!! $"""
                // Theme initialization script
                (function() {{
                    // Theme configuration
                    const darkTheme = '%s{darkTheme}';
                    const lightTheme = '%s{lightTheme}';
                    
                    // Get theme from localStorage or default to dark theme
                    let savedTheme = localStorage.getItem('theme') || darkTheme;
                    
                    // Function to apply theme
                    function applyTheme(theme) {{
                        document.documentElement.setAttribute('data-theme', theme);
                        localStorage.setItem('theme', theme);
                    }}
                    
                    // Apply saved theme
                    applyTheme(savedTheme);
                    
                    // Initialize theme toggles once DOM is loaded
                    document.addEventListener('DOMContentLoaded', function() {{
                        const themeToggles = document.querySelectorAll('.theme-controller');
                        
                        // Update checkbox state based on current theme
                        themeToggles.forEach(toggle => {{
                            toggle.checked = (savedTheme === lightTheme);
                        }});

                        // Add event listeners to all theme toggles
                        themeToggles.forEach(toggle => {{
                            toggle.addEventListener('change', function() {{
                                const newTheme = this.checked ? lightTheme : darkTheme;
                                applyTheme(newTheme);
                                
                                // Sync all other toggles
                                themeToggles.forEach(otherToggle => {{
                                    if (otherToggle !== this) {{
                                        otherToggle.checked = this.checked;
                                    }}
                                }});
                            }});
                        }});
                    }});
                }})();
            """]
        ]
        body [] [
            navBar
            yield! bodyCnt
        ]
    ]

let render (ctx : SiteContents) cnt =
  let disableLiveRefresh = ctx.TryGetValue<Postloader.PostConfig> () |> Option.map (fun n -> n.disableLiveRefresh) |> Option.defaultValue false
  cnt
  |> HtmlElement.ToString
  |> fun n -> if disableLiveRefresh then n else injectWebsocketCode n

let published (post: Postloader.Post) =
    post.published
    |> Option.defaultValue System.DateTime.Now
    |> fun n -> n.ToString("yyyy-MM-dd")

let postLayout (useSummary: bool) (post: Postloader.Post) =
    div [Class "card bg-base-100 shadow-xl mb-6"] [
        div [Class "card-body"] [
            div [Class "text-center"] [
                h2 [Class "card-title justify-center"] [ a [Href post.link] [!! post.title]]
                p [Class "text-sm opacity-70"] [
                    a [Href "#"] [!! (defaultArg post.author "")]
                    !! (sprintf " on %s" (published post))
                ]
            ]
            div [Class "mt-4 prose max-w-none"] [
                if useSummary then
                    !! post.summary
                else
                    !! post.content
            ]
        ]
    ]