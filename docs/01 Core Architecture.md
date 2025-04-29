# Oxpecker Core Architecture

## Introduction

This document details the core architecture for the FlightDeck platform based on Oxpecker, a lightweight, functional-first web framework for F#. This architecture replaces the static site generation approach with a dynamic server capable of handling both content rendering and API requests while maintaining the performance benefits of the original design.

## Architectural Components

### Server Structure

The Oxpecker core architecture consists of the following key components:

```mermaid
%%{init: {'theme': 'dark'}}%%
flowchart TB
    subgraph Server["Oxpecker Server"]
        direction TB
        Program["Program.fs<br>Application Entry Point"]
        Handlers["Handlers.fs<br>HTTP Request Processing"]
        Domain["Domain.fs<br>Core Domain Types"]
        Services["Services.fs<br>Business Logic"]
        Views["Views.fs<br>HTML Rendering"]
        Storage["Storage.fs<br>Data Access"]
        
        Program --> Handlers
        Handlers --> Views
        Handlers --> Services
        Services --> Domain
        Services --> Storage
        Views --> Domain
    end
    
    Client[("Client<br>(Browser)")] <--> Handlers
    Storage <--> FileSystem["File System"]
    Storage <--> Database[("Database<br>(Optional)")]
```

### Request Processing Flow

The request processing flow in Oxpecker follows a clean, functional pipeline:

```mermaid
%%{init: {'theme': 'dark'}}%%
sequenceDiagram
    participant C as Client
    participant H as HTTP Handlers
    participant S as Services
    participant V as Views
    participant St as Storage
    
    C->>+H: HTTP Request
    H->>+S: Process Request
    S->>+St: Fetch/Update Data
    St-->>-S: Data Response
    S-->>-H: Domain Objects
    
    alt HTML Response
        H->>+V: Render View
        V-->>-H: HTML Content
        H-->>C: HTML Response
    else API Response
        H-->>C: JSON Response
    end
```

## Implementation Guide

### Core Project Structure

```
src/
├── FlightDeck.Core/
│   ├── Domain.fs       # Domain types
│   ├── Services.fs     # Business logic
│   └── Storage.fs      # Data access functions
│
├── FlightDeck.Web/
│   ├── Program.fs      # Application entry point
│   ├── Handlers.fs     # HTTP handlers
│   ├── Views.fs        # HTML rendering
│   ├── Endpoints.fs    # URL definitions
│   └── Error.fs        # Error handling
│
└── FlightDeck.Shared/  # Shared client/server code
    └── Domain.fs       # Shared domain types
```

### Entry Point (Program.fs)

```fsharp
module FlightDeck.Web.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Oxpecker
open FlightDeck.Web.Endpoints

// Configure services
let configureServices (services: IServiceCollection) =
    services
        .AddSingleton<IStorageProvider, FileSystemStorageProvider>()
        .AddSingleton<IContentService, ContentService>()
        // Add other services
        .AddRouting() 
        |> ignore

// Define application
[<EntryPoint>]
let main args =
    webHost args {
        add_service configureServices
        app_config (fun app ->
            app.UseRouting()
               .UseStaticFiles()
               .UseOxpecker(endpoints)
               .UseExceptionHandler(Error.serverError)
        )
    }
    0
```

### Endpoints Configuration (Endpoints.fs)

```fsharp
module FlightDeck.Web.Endpoints

open Oxpecker
open FlightDeck.Web.Handlers

// Define all application endpoints
let endpoints = 
    [
        // Content pages
        GET [
            route "/" contentHome
            route "/content/{slug}" contentPage
        ]
        
        // API endpoints
        POST [
            route "/api/content" createContent
        ]
        
        PUT [
            route "/api/content/{id}" updateContent
        ]
        
        DELETE [
            route "/api/content/{id}" deleteContent
        ]
        
        // Admin interface
        GET [
            route "/admin" adminDashboard
            route "/admin/content" adminContent
            route "/admin/settings" adminSettings
        ]
        
        // Presentations (FsReveal integration)
        GET [
            route "/presentations" presentationsList
            route "/presentations/{id}" viewPresentation
        ]
        
        POST [
            route "/presentations" createPresentation
        ]
    ]
```

### HTTP Handlers (Handlers.fs)

```fsharp
module FlightDeck.Web.Handlers

open Oxpecker
open FlightDeck.Core.Domain
open FlightDeck.Core.Services
open FlightDeck.Web.Views

// Content handlers
let contentHome : HttpHandler =
    fun ctx -> task {
        let contentService = ctx.GetService<IContentService>()
        let! content = contentService.GetHomePage()
        
        match content with
        | Some page -> 
            return! renderPage page ctx
        | None -> 
            return! (setStatusCode 404 >=> Error.notFound) ctx
    }

let contentPage : HttpHandler =
    routef (fun (slug: string) ctx -> task {
        let contentService = ctx.GetService<IContentService>()
        let! content = contentService.GetPageBySlug(slug)
        
        match content with
        | Some page -> 
            return! renderPage page ctx
        | None -> 
            return! (setStatusCode 404 >=> Error.notFound) ctx
    })

// API handlers
let createContent : HttpHandler =
    bindJson<CreateContentRequest> (fun request ctx -> task {
        let contentService = ctx.GetService<IContentService>()
        
        try
            let! content = contentService.CreateContent(request)
            return! json content ctx
        with ex ->
            return! (setStatusCode 500 >=> json {| error = ex.Message |}) ctx
    })

// More handlers for other endpoints...
```

### View Rendering (Views.fs)

```fsharp
module FlightDeck.Web.Views

open Oxpecker.ViewEngine
open FlightDeck.Core.Domain

// Master layout
let masterLayout (title: string) (content: Node list) =
    html [ _lang "en" ] [
        head [] [
            meta [ _charset "utf-8" ]
            meta [ _name "viewport"; _content "width=device-width, initial-scale=1.0" ]
            title [] [ rawText title ]
            link [ _rel "stylesheet"; _href "/css/styles.css" ]
        ]
        body [] [
            header [ _class "site-header" ] [
                div [ _class "container" ] [
                    a [ _href "/"; _class "site-logo" ] [ rawText "FlightDeck" ]
                    nav [ _class "site-nav" ] [
                        a [ _href "/content/about" ] [ rawText "About" ]
                        a [ _href "/content/blog" ] [ rawText "Blog" ]
                        a [ _href "/presentations" ] [ rawText "Presentations" ]
                        a [ _href "/admin" ] [ rawText "Admin" ]
                    ]
                ]
            ]
            main [ _class "container" ] content
            footer [ _class "site-footer" ] [
                div [ _class "container" ] [
                    rawText "© 2025 FlightDeck"
                ]
            ]
            
            // Script for reactive components
            script [ _src "/js/app.js"; _defer ] []
        ]
    ]

// Page rendering
let renderPage (page: ContentPage) =
    masterLayout page.Title [
        article [ _class "page-content" ] [
            h1 [] [ rawText page.Title ]
            
            // Render content based on format
            match page.Format with
            | ContentFormat.Markdown ->
                div [ 
                    _class "markdown-content"
                    _id $"content-{page.Id}"
                    attr "data-content-id" page.Id
                ] [ rawText page.RenderedContent ]
            | ContentFormat.Html ->
                div [ 
                    _class "html-content"
                    _id $"content-{page.Id}"
                    attr "data-content-id" page.Id
                ] [ rawText page.Content ]
            
            // Metadata
            div [ _class "page-meta" ] [
                span [ _class "page-date" ] [ rawText (page.UpdatedAt.ToString("yyyy-MM-dd")) ]
                span [ _class "page-author" ] [ rawText page.Author ]
            ]
        ]
    ]
    |> htmlView

// Admin dashboard
let renderAdminDashboard (stats: DashboardStats) =
    masterLayout "Admin Dashboard" [
        div [ _class "admin-header" ] [
            h1 [] [ rawText "Admin Dashboard" ]
        ]
        
        div [ 
            _id "admin-app" 
            attr "data-stats" (System.Text.Json.JsonSerializer.Serialize(stats))
        ] []  // Mount point for Oxpecker.Solid admin app
    ]
    |> htmlView

// More view functions...
```

### Core Domain Model (Domain.fs)

```fsharp
module FlightDeck.Core.Domain

open System

// Content types
type ContentFormat =
    | Markdown
    | Html

type ContentStatus =
    | Draft
    | Published
    | Archived

type ContentPage = {
    Id: string
    Slug: string
    Title: string
    Description: string option
    Content: string
    RenderedContent: string
    Format: ContentFormat
    Status: ContentStatus
    CreatedAt: DateTime
    UpdatedAt: DateTime
    Author: string
    Tags: string list
}

// Request/response types
type CreateContentRequest = {
    Title: string
    Slug: string option
    Description: string option
    Content: string
    Format: ContentFormat
    Tags: string list
}

type UpdateContentRequest = {
    Id: string
    Title: string option
    Slug: string option
    Description: string option
    Content: string option
    Format: ContentFormat option
    Status: ContentStatus option
    Tags: string list option
}

// Dashboard statistics
type DashboardStats = {
    TotalPages: int
    PublishedPages: int
    DraftPages: int
    RecentEdits: {| Title: string; Id: string; UpdatedAt: DateTime |} list
}

// Other domain types for different features...
```

### Business Services (Services.fs)

```fsharp
module FlightDeck.Core.Services

open System
open System.Threading.Tasks
open FlightDeck.Core.Domain
open FlightDeck.Core.Storage

// Content service interface
type IContentService =
    abstract member GetHomePage: unit -> Task<ContentPage option>
    abstract member GetPageBySlug: string -> Task<ContentPage option>
    abstract member GetPageById: string -> Task<ContentPage option>
    abstract member GetAllPages: unit -> Task<ContentPage list>
    abstract member CreateContent: CreateContentRequest -> Task<ContentPage>
    abstract member UpdateContent: UpdateContentRequest -> Task<ContentPage option>
    abstract member DeleteContent: string -> Task<bool>
    abstract member GetDashboardStats: unit -> Task<DashboardStats>

// Implementation
type ContentService(storage: IStorageProvider) =
    
    let renderMarkdown (content: string) =
        // Use a Markdown library to render to HTML
        // For example, with Markdig:
        let pipeline = Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build()
        Markdig.Markdown.ToHtml(content, pipeline)
    
    interface IContentService with
        member _.GetHomePage() =
            storage.GetPageBySlug("home")
            
        member _.GetPageBySlug(slug) =
            storage.GetPageBySlug(slug)
            
        member _.GetPageById(id) =
            storage.GetPageById(id)
            
        member _.GetAllPages() =
            storage.GetAllPages()
            
        member _.CreateContent(request) = task {
            let id = Guid.NewGuid().ToString()
            let slug = request.Slug |> Option.defaultValue (request.Title.ToLower().Replace(" ", "-"))
            let now = DateTime.UtcNow
            
            let rendered = 
                match request.Format with
                | ContentFormat.Markdown -> renderMarkdown request.Content
                | ContentFormat.Html -> request.Content
            
            let page = {
                Id = id
                Slug = slug
                Title = request.Title
                Description = request.Description
                Content = request.Content
                RenderedContent = rendered
                Format = request.Format
                Status = ContentStatus.Draft
                CreatedAt = now
                UpdatedAt = now
                Author = "System" // Would come from auth
                Tags = request.Tags
            }
            
            do! storage.SavePage(page)
            return page
        }
            
        // Other method implementations...
```

### Storage Layer (Storage.fs)

```fsharp
module FlightDeck.Core.Storage

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open FlightDeck.Core.Domain

// Storage provider interface
type IStorageProvider =
    abstract member GetPageBySlug: string -> Task<ContentPage option>
    abstract member GetPageById: string -> Task<ContentPage option>
    abstract member GetAllPages: unit -> Task<ContentPage list>
    abstract member SavePage: ContentPage -> Task<unit>
    abstract member DeletePage: string -> Task<bool>

// File system implementation
type FileSystemStorageProvider(contentDir: string) =
    
    do 
        if not (Directory.Exists contentDir) then
            Directory.CreateDirectory contentDir |> ignore
    
    let getPagePath id = Path.Combine(contentDir, $"{id}.json")
    
    let slugIndex = 
        // Load or create slug index mapping slugs to ids
        let indexPath = Path.Combine(contentDir, "_slugindex.json")
        if File.Exists indexPath then
            JsonSerializer.Deserialize<Map<string, string>>(File.ReadAllText(indexPath))
        else
            Map.empty
    
    let saveSlugIndex (index: Map<string, string>) =
        let indexPath = Path.Combine(contentDir, "_slugindex.json")
        File.WriteAllText(indexPath, JsonSerializer.Serialize(index))
    
    let loadPage id =
        task {
            let path = getPagePath id
            if File.Exists path then
                let! content = File.ReadAllTextAsync(path)
                return JsonSerializer.Deserialize<ContentPage>(content) |> Some
            else
                return None
        }
    
    interface IStorageProvider with
        member _.GetPageBySlug(slug) = task {
            match Map.tryFind slug slugIndex with
            | Some id -> return! loadPage id
            | None -> return None
        }
            
        member _.GetPageById(id) =
            loadPage id
            
        member _.GetAllPages() = task {
            let files = Directory.GetFiles(contentDir, "*.json")
                        |> Array.filter (fun f -> not (f.EndsWith("_slugindex.json")))
            
            let! results = files 
                         |> Array.map (fun f -> task {
                             try 
                                 let! content = File.ReadAllTextAsync(f)
                                 return JsonSerializer.Deserialize<ContentPage>(content) |> Some
                             with _ -> 
                                 return None
                         })
                         |> Task.WhenAll
                         
            return results 
                   |> Array.choose id
                   |> Array.toList
        }
            
        member _.SavePage(page) = task {
            let path = getPagePath page.Id
            let! content = JsonSerializer.SerializeAsync(page)
            
            use fileStream = new FileStream(path, FileMode.Create)
            do! content.CopyToAsync(fileStream)
            
            // Update slug index
            let updatedIndex = slugIndex |> Map.add page.Slug page.Id
            saveSlugIndex updatedIndex
        }
            
        member _.DeletePage(id) = task {
            match! loadPage id with
            | Some page ->
                let path = getPagePath id
                if File.Exists path then
                    File.Delete path
                    
                    // Update slug index
                    let updatedIndex = slugIndex |> Map.remove page.Slug
                    saveSlugIndex updatedIndex
                    
                    return true
                else
                    return false
            | None -> 
                return false
        }
```

### Error Handling (Error.fs)

```fsharp
module FlightDeck.Web.Error

open Oxpecker
open Oxpecker.ViewEngine

// Not found (404) handler
let notFound : HttpHandler =
    fun ctx -> task {
        let html =
            html [ _lang "en" ] [
                head [] [
                    meta [ _charset "utf-8" ]
                    meta [ _name "viewport"; _content "width=device-width, initial-scale=1.0" ]
                    title [] [ rawText "Not Found - FlightDeck" ]
                    link [ _rel "stylesheet"; _href "/css/styles.css" ]
                ]
                body [] [
                    div [ _class "error-container" ] [
                        h1 [] [ rawText "404" ]
                        h2 [] [ rawText "Page Not Found" ]
                        p [] [ rawText "The page you are looking for could not be found." ]
                        a [ _href "/"; _class "button" ] [ rawText "Go Home" ]
                    ]
                ]
            ]
        
        return! (setStatusCode 404 >=> htmlView html) ctx
    }

// Server error (500) handler
let serverError : HttpHandler =
    fun ctx -> task {
        let html =
            html [ _lang "en" ] [
                head [] [
                    meta [ _charset "utf-8" ]
                    meta [ _name "viewport"; _content "width=device-width, initial-scale=1.0" ]
                    title [] [ rawText "Server Error - FlightDeck" ]
                    link [ _rel "stylesheet"; _href "/css/styles.css" ]
                ]
                body [] [
                    div [ _class "error-container" ] [
                        h1 [] [ rawText "500" ]
                        h2 [] [ rawText "Server Error" ]
                        p [] [ rawText "Something went wrong. Please try again later." ]
                        a [ _href "/"; _class "button" ] [ rawText "Go Home" ]
                    ]
                ]
            ]
        
        return! (setStatusCode 500 >=> htmlView html) ctx
    }
```

## Performance Considerations

### Response Caching

Oxpecker allows for HTTP response caching to maintain performance similar to static site generation:

```fsharp
open Microsoft.AspNetCore.Http
open Microsoft.Net.Http.Headers

// Add cache headers to responses
let withCaching (durationInSeconds: int) (handler: HttpHandler) : HttpHandler =
    fun ctx -> task {
        ctx.Response.GetTypedHeaders().CacheControl <-
            CacheControlHeaderValue(
                Public = true,
                MaxAge = TimeSpan.FromSeconds(float durationInSeconds))
        
        return! handler ctx
    }

// Use in handlers
let cachedContentPage : HttpHandler =
    contentPage |> withCaching 3600  // Cache for 1 hour
```

### Static Asset Handling

Static assets should be served efficiently:

```fsharp
// In Program.fs configuration
app.UseStaticFiles(StaticFileOptions(
        OnPrepareResponse = fun ctx ->
            ctx.Context.Response.Headers.Add(
                HeaderNames.CacheControl, 
                "public, max-age=31536000")  // Cache for 1 year
    ))
```

## Security Implementation

### Content Security Policy

Implement a robust Content Security Policy:

```fsharp
// Middleware to add security headers
let securityHeaders (next: RequestDelegate) (ctx: HttpContext) =
    // Add Content-Security-Policy header
    ctx.Response.Headers.Add("Content-Security-Policy", 
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:;")
    
    // Add other security headers
    ctx.Response.Headers.Add("X-Frame-Options", "DENY")
    ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff")
    ctx.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin")
    
    next.Invoke(ctx)

// In Program.fs configuration
app.Use(securityHeaders)
```

## Conclusion

This Oxpecker-based architecture provides a solid foundation for the FlightDeck platform, combining the performance benefits of static site generation with the flexibility of dynamic content rendering and API endpoints. The modular, functional approach ensures code remains maintainable and testable while enabling a smooth transition from the previous architecture.

By leveraging Oxpecker's lightweight nature and the functional programming paradigm of F#, the architecture maintains excellent performance characteristics while opening up new possibilities for dynamic features and interactive components.