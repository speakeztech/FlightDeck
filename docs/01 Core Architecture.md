## Frontmatter Parsing with Tomlyn

FlightDeck uses the Tomlyn library to implement proper TOML-based frontmatter parsing for content files. This provides a more structured and strongly-typed approach to metadata compared to traditional YAML frontmatter.

### TOML Frontmatter Implementation

```fsharp
module FlightDeck.Core.Frontmatter

open System
open Tomlyn
open Tomlyn.Model

// Types for strongly-typed frontmatter
type Frontmatter = {
    Title: string
    Author: string option
    Date: DateTimeOffset option
    FeaturedPath: string option
    Featured: string option
    OgFeatured: string option
    Emblem: string option
    Description: string option
    Draft: bool
    Tags: string list
    Categories: string list
}

// Parse frontmatter from content file
let parseFrontmatter (content: string) : Frontmatter option =
    let frontmatterPattern = "+++([\\s\\S]*?)+++"
    
    let regexMatch = System.Text.RegularExpressions.Regex.Match(content, frontmatterPattern)
    if regexMatch.Success then
        let tomlContent = regexMatch.Groups.[1].Value
        
        try
            // Parse TOML
            let toml = Toml.Parse(tomlContent)
            let model = toml.ToModel()
            
            // Extract values from TOML model
            let title = model.GetString("title", "")
            let author = model.TryGetString("author")
            let date = 
                model.TryGetString("date")
                |> Option.bind (fun dateStr ->
                    try Some (DateTimeOffset.Parse(dateStr))
                    with _ -> None
                )
            let featuredPath = model.TryGetString("featuredpath")
            let featured = model.TryGetString("featured")
            let ogFeatured = model.TryGetString("ogfeatured")
            let emblem = model.TryGetString("emblem")
            let description = model.TryGetString("description")
            let draft = model.GetBoolean("draft", false)
            
            // Handle arrays
            let tags = 
                match model.TryGetValue("tags") with
                | Some (:? TomlArray as arr) -> 
                    arr.Items 
                    |> Seq.choose (fun v -> match v with | :? string as s -> Some s | _ -> None)
                    |> Seq.toList
                | _ -> []
                
            let categories = 
                match model.TryGetValue("categories") with
                | Some (:? TomlArray as arr) -> 
                    arr.Items 
                    |> Seq.choose (fun v -> match v with | :? string as s -> Some s | _ -> None)
                    |> Seq.toList
                | _ -> []
            
            Some {
                Title = title
                Author = author
                Date = date
                FeaturedPath = featuredPath
                Featured = featured
                OgFeatured = ogFeatured
                Emblem = emblem
                Description = description
                Draft = draft
                Tags = tags
                Categories = categories
            }
        with _ ->
            None
    else
        None

// Strip frontmatter from content
let stripFrontmatter (content: string) : string =
    let frontmatterPattern = "+++[\\s\\S]*?+++"
    System.Text.RegularExpressions.Regex.Replace(content, frontmatterPattern, "").Trim()

// Create frontmatter from metadata
let createFrontmatter (metadata: Frontmatter) : string =
    let tomlObj = TomlTable()
    
    // Add all properties
    tomlObj.["title"] <- metadata.Title
    metadata.Author |> Option.iter (fun v -> tomlObj.["author"] <- v)
    metadata.Date |> Option.iter (fun v -> tomlObj.["date"] <- v.ToString("o"))
    metadata.FeaturedPath |> Option.iter (fun v -> tomlObj.["featuredpath"] <- v)
    metadata.Featured |> Option.iter (fun v -> tomlObj.["featured"] <- v)
    metadata.OgFeatured |> Option.iter (fun v -> tomlObj.["ogfeatured"] <- v)
    metadata.Emblem |> Option.iter (fun v -> tomlObj.["emblem"] <- v)
    metadata.Description |> Option.iter (fun v -> tomlObj.["description"] <- v)
    tomlObj.["draft"] <- metadata.Draft
    
    // Add arrays
    let tagsArray = TomlArray()
    for tag in metadata.Tags do
        tagsArray.Add(tag)
    tomlObj.["tags"] <- tagsArray
    
    let categoriesArray = TomlArray()
    for category in metadata.Categories do
        categoriesArray.Add(category)
    tomlObj.["categories"] <- categoriesArray
    
    // Format as TOML
    "+++\n" + Toml.FromModel(tomlObj) + "+++"
```

### Example TOML Frontmatter

YAML-style frontmatter:

```yaml
---
title: "Bragi In-Ear Wireless Audio with Pulse Oximeter"
author: "Houston Haynes"
date: '2019-06-20T14:59:43+05:30'
featuredpath: img
featured: Bragi.png
ogfeatured: H3_og_wide_Bragi.png
emblem: iot.png
description: A Kickstarter Experiment - measuring activity data through optical pulse oximeters placed in ear canal
draft: false
tags:
  - Android
  - API
  - Bluetooth
  - IoT
  - health
  - fitness
  - mobile
  - Bragi
categories:
  - Sidebar
---
```

Converted to TOML frontmatter:

```toml
+++
title = "Bragi In-Ear Wireless Audio with Pulse Oximeter"
author = "Houston Haynes"
date = "2019-06-20T14:59:43+05:30"
featuredpath = "img"
featured = "Bragi.png"
ogfeatured = "H3_og_wide_Bragi.png"
emblem = "iot.png"
description = "A Kickstarter Experiment - measuring activity data through optical pulse oximeters placed in ear canal"
draft = false
tags = [
  "Android",
  "API",
  "Bluetooth",
  "IoT",
  "health",
  "fitness",
  "mobile",
  "Bragi"
]
categories = [
  "Sidebar"
]
+++
```

### Usage in Content Service

```fsharp
// In ContentService.fs
let parseContentFile (filePath: string) =
    let content = System.IO.File.ReadAllText(filePath)
    
    // Parse frontmatter
    let frontmatter = Frontmatter.parseFrontmatter content
    
    match frontmatter with
    | Some metadata ->
        // Get the actual content without frontmatter
        let contentBody = Frontmatter.stripFrontmatter content
        
        // Create content page object
        let contentPage = {
            Id = System.IO.Path.GetFileNameWithoutExtension(filePath)
            Slug = System.IO.Path.GetFileNameWithoutExtension(filePath).ToLower()
            Title = metadata.Title
            Description = metadata.Description
            Content = contentBody
            RenderedContent = renderContent contentBody (Some "Markdown")
            Format = ContentFormat.Markdown
            Status = if metadata.Draft then ContentStatus.Draft else ContentStatus.Published
            CreatedAt = metadata.Date |> Option.defaultValue DateTimeOffset.UtcNow
            UpdatedAt = DateTimeOffset.UtcNow
            Author = metadata.Author |> Option.defaultValue "Unknown"
            Tags = metadata.Tags
        }
        
        Some contentPage
        
    | None ->
        // Failed to parse frontmatter
        None
```

The TOML-based frontmatter provides several advantages over YAML:

1. **Type Safety**: TOML has more explicit typing rules, reducing ambiguity
2. **Performance**: Typically faster parsing than YAML
3. **Simplicity**: More readable and less complex than YAML
4. **Consistency**: Fewer edge cases and surprising behaviors
5. **Integration**: Natural fit with F#'s type system
