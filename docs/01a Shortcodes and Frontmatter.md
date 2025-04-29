# Shortcodes and TOML Frontmatter

This document details the implementation of TOML frontmatter and shortcodes in the FlightDeck platform, providing structured metadata and enhanced content capabilities.

## TOML Frontmatter

FlightDeck uses the Tomlyn library to implement proper TOML-based frontmatter parsing for content files, providing a more structured and strongly-typed approach compared to traditional YAML frontmatter.

### TOML Frontmatter Example

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
    let frontmatterPattern = "\\+\\+\\+([\\s\\S]*?)\\+\\+\\+"
    
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
    let frontmatterPattern = "\\+\\+\\+[\\s\\S]*?\\+\\+\\+"
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

From there the frontmatter elements can be parsed to provide site and page elements at call-time.

## Shortcode System

FlightDeck implements shortcodes using the `:::` block notation from CommonMark, maintaining consistency with FsReveal and ensuring a clean, familiar syntax for content authors.

### Shortcode Implementation

```fsharp
module FlightDeck.Core.Markdown.Shortcodes

open System
open System.Text.RegularExpressions
open Markdig
open Markdig.Parsers
open Markdig.Renderers
open Markdig.Syntax

// Shortcode block parser
type ShortcodeBlockParser() =
    inherit BlockParser()
    
    let shortcodeRegex = Regex(@"^:::(\s+)?([a-zA-Z0-9_-]+)(\s+)?(.*)?$", RegexOptions.Compiled)
    let endShortcodeRegex = Regex(@"^:::(\s+)?$", RegexOptions.Compiled)
    
    override _.TryOpen(processor: BlockProcessor) =
        // Try to match the opening of a shortcode block
        let line = processor.Line
        let match' = shortcodeRegex.Match(line.ToString())
        
        if match'.Success then
            // Extract shortcode name and attributes
            let shortcodeName = match'.Groups.[2].Value
            let attributes = 
                if match'.Groups.[4].Success then 
                    match'.Groups.[4].Value
                else 
                    ""
            
            // Create a shortcode block
            let block = ShortcodeBlock(shortcodeName, attributes)
            processor.NewBlocks.Push(block)
            
            // Skip the current line as we've processed it
            processor.GoToColumn(line.End)
            
            BlockState.ContinueDiscard
        else
            BlockState.None
    
    override _.TryContinue(processor: BlockProcessor, block: Block) =
        if not (block :? ShortcodeBlock) then
            BlockState.None
        else
            let line = processor.Line
            let match' = endShortcodeRegex.Match(line.ToString())
            
            if match'.Success then
                // We found the end of the shortcode block
                processor.GoToColumn(line.End)
                BlockState.BreakDiscard
            else
                // Continue collecting content for the shortcode
                let shortcodeBlock = block :?> ShortcodeBlock
                let content = line.ToString()
                shortcodeBlock.Content.Add(content)
                
                processor.GoToColumn(line.End)
                BlockState.ContinueDiscard

// Shortcode block representation
and ShortcodeBlock(name: string, attributes: string) =
    inherit LeafBlock()
    
    member val Name = name with get
    member val Attributes = attributes with get
    member val Content = System.Collections.Generic.List<string>() with get

// Shortcode HTML renderer
type ShortcodeHtmlRenderer() =
    interface IMarkdownObjectRenderer with
        member _.Write(renderer: MarkdownObjectRenderer, markdown: MarkdownObject) =
            let shortcode = markdown :?> ShortcodeBlock
            let htmlRenderer = renderer :?> HtmlRenderer
            
            // Start the shortcode rendering
            htmlRenderer.Write("<div class=\"shortcode ")
                       .Write(shortcode.Name)
                       .Write("\"")
            
            // Add any attributes
            if not (String.IsNullOrWhiteSpace(shortcode.Attributes)) then
                let attrs = shortcode.Attributes.Trim()
                htmlRenderer.Write(" data-attrs=\"")
                           .WriteEscape(attrs)
                           .Write("\"")
            
            htmlRenderer.WriteLine(">")
            
            // Process the shortcode content based on name
            match shortcode.Name.ToLowerInvariant() with
            | "quote" -> renderQuote htmlRenderer shortcode
            | "info" -> renderInfo htmlRenderer shortcode
            | "warning" -> renderWarning htmlRenderer shortcode
            | "code" -> renderCode htmlRenderer shortcode
            | "image" -> renderImage htmlRenderer shortcode
            | "gallery" -> renderGallery htmlRenderer shortcode
            | _ -> renderGenericShortcode htmlRenderer shortcode
            
            // End the shortcode
            htmlRenderer.WriteLine("</div>")

// Create a markdown pipeline with shortcode support
let createMarkdownPipeline() =
    let pipeline = new MarkdownPipelineBuilder()
                        .UseAdvancedExtensions()
                        .Build()
                        
    // Add the shortcode parser and renderer
    let parser = pipeline.BlockParsers.Find<BlockParser>()
    pipeline.BlockParsers.InsertBefore<ParagraphBlockParser>(ShortcodeBlockParser())
    
    // Register the renderer
    pipeline.ObjectRenderers.AddIfNotAlready<ShortcodeHtmlRenderer>() |> ignore
    
    pipeline

// Process markdown with shortcodes
let processMarkdown (markdown: string) =
    let pipeline = createMarkdownPipeline()
    Markdown.ToHtml(markdown, pipeline)

// Render specific shortcode types
// Quote shortcode renderer
let private renderQuote (renderer: HtmlRenderer) (shortcode: ShortcodeBlock) =
    renderer.WriteLine("<blockquote class=\"shortcode-quote\">")
    
    for line in shortcode.Content do
        renderer.WriteLine("<p>").WriteEscape(line).WriteLine("</p>")
    
    // Look for citation attribute
    let citation = 
        if shortcode.Attributes.Contains("citation=") then
            let match' = Regex.Match(shortcode.Attributes, "citation=\"([^\"]+)\"")
            if match'.Success then Some match'.Groups.[1].Value else None
        else
            None
            
    // Add citation if present
    citation |> Option.iter (fun cite ->
        renderer.Write("<footer>").WriteEscape(cite).WriteLine("</footer>")
    )
    
    renderer.WriteLine("</blockquote>")

// More shortcode renderers would follow...
```

### Shortcode Examples

FlightDeck supports various shortcodes for enhanced content. Here are some examples:

#### Quote Shortcode

```markdown
::: quote citation="Albert Einstein"
Imagination is more important than knowledge. Knowledge is limited. Imagination encircles the world.
:::
```

Renders as:

```html
<div class="shortcode quote">
  <blockquote class="shortcode-quote">
    <p>Imagination is more important than knowledge. Knowledge is limited. Imagination encircles the world.</p>
    <footer>Albert Einstein</footer>
  </blockquote>
</div>
```

#### Info Box

```markdown
::: info
This is an important piece of information that you should know about.
Multiple paragraphs are supported.
:::
```

#### Warning Box

```markdown
::: warning
Be careful when editing this section. Changes may affect other components.
:::
```

#### Code with Syntax Highlighting

```markdown
::: code lang="fsharp"
let add x y = x + y
let result = add 5 10
printfn "Result: %d" result
:::
```

#### Image with Caption

```markdown
::: image src="/images/diagram.png" alt="Architecture Diagram" caption="System Architecture Overview" width="800"
:::
```

#### Gallery

```markdown
::: gallery
/images/photo1.jpg | Mountain landscape
/images/photo2.jpg | Ocean sunset
/images/photo3.jpg | City skyline
:::
```

## Integration with Content System

The TOML frontmatter and shortcode system integrate with the content processing pipeline:

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
        
        // Process markdown with shortcodes
        let renderedContent = Markdown.Shortcodes.processMarkdown contentBody
        
        // Create content page object
        let contentPage = {
            Id = System.IO.Path.GetFileNameWithoutExtension(filePath)
            Slug = System.IO.Path.GetFileNameWithoutExtension(filePath).ToLower()
            Title = metadata.Title
            Description = metadata.Description
            Content = contentBody
            RenderedContent = renderedContent
            Format = ContentFormat.Markdown
            Status = if metadata.Draft then ContentStatus.Draft else ContentStatus.Published
            CreatedAt = metadata.Date |> Option.defaultValue DateTimeOffset.UtcNow |> fun d -> d.DateTime
            UpdatedAt = DateTimeOffset.UtcNow.DateTime
            Author = metadata.Author |> Option.defaultValue "Unknown"
            Tags = metadata.Tags
        }
        
        Some contentPage
        
    | None ->
        // Failed to parse frontmatter
        None
```

## Benefits

### TOML Frontmatter Benefits

1. **Type Safety**: TOML has more explicit typing rules, reducing ambiguity
2. **Performance**: Typically faster parsing than YAML
3. **Simplicity**: More readable and less complex than YAML
4. **Consistency**: Fewer edge cases and surprising behaviors
5. **Integration**: Natural fit with F#'s type system

### Shortcode System Benefits

1. **Consistency with FsReveal**: Uses the same `:::` block syntax as FsReveal presentations
2. **Common Markdown Standard**: Follows established CommonMark syntax
3. **Extensibility**: Easy to add new shortcode types without changing the core parser
4. **Clean syntax**: More readable than HTML embedded in markdown
5. **Rich formatting**: Supports complex layout components while maintaining markdown simplicity

## Conclusion

The combination of TOML frontmatter and CommonMark shortcodes creates a powerful, consistent content authoring experience in FlightDeck. This approach maintains the simplicity of markdown while providing structured metadata and rich formatting capabilities. By aligning with FsReveal's patterns, FlightDeck ensures a cohesive syntax across all platform components.
