#r "nuget: Fornax.Core, 0.15.1"

type SiteInfo = {
    title: string
    description: string
    postPageSize: int
    lightTheme: string
    darkTheme: string
}

let loader (projectRoot: string) (siteContent: SiteContents) =
    let siteInfo =
        { title = "Sample FlightDeck Site";
          description = "A modern static site built with FlightDeck"
          postPageSize = 5
          lightTheme = "light"
          darkTheme = "dark" }
    siteContent.Add(siteInfo)

    siteContent
