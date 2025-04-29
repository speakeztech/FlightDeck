# Domain Model Design for Event Sourcing

## Introduction

This document outlines a streamlined domain model design for FlightDeck's event sourcing system, addressing the "DU sprawl" issue observed in the CollabGateway example while maintaining type safety and expressive power.

## Key Design Principles

1. **Reduce Type Proliferation**: Minimize the number of discriminated unions (DUs) and types.
2. **Improve Flexibility**: Create a more adaptable event model that doesn't require code changes for new event types.
3. **Maintain Type Safety**: Preserve F#'s type checking capabilities where they add the most value.
4. **Enable Automatic Telemetry**: Support automatic generation of telemetry from page frontmatter.

## Event Model Design

### The Problem with DU Sprawl

The CollabGateway example demonstrates "DU sprawl" with numerous specialized event types:

```fsharp
// Example of DU sprawl from CollabGateway
type ButtonEventCase =
    | DataPolicyAcceptButtonClicked of ButtonEvent
    | DataPolicyDeclineButtonClicked of ButtonEvent
    | HomeButtonClicked of ButtonEvent
    | HomeProjectButtonClicked of ButtonEvent
    // ... dozens more cases for each button
```

This approach creates maintenance challenges:
- Adding a new button requires code changes in multiple files
- Event types proliferate unnecessarily
- The system is rigid and difficult to extend

### Streamlined Event Model

Instead, we'll use a more generic approach with strong typing where it matters:

```fsharp
// Core event types
type EventId = Guid
type StreamId = string
type Timestamp = DateTime
type EventMetadata = Map<string, string>

// Unified event model with generic fields
type UserEvent =
    | PageViewed of pageId:string * metadata:EventMetadata * timestamp:Timestamp
    | ElementInteraction of elementId:string * interactionType:string * metadata:EventMetadata * timestamp:Timestamp
    | FormSubmitted of formId:string * formData:Map<string, string> * metadata:EventMetadata * timestamp:Timestamp
    | SystemEvent of eventType:string * data:Map<string, string> * metadata:EventMetadata * timestamp:Timestamp
```

This approach:
- Reduces dozens of DU cases to just 4 primary event types
- Uses string identifiers for flexible extensibility
- Maintains type safety for the event structure itself
- Allows new interaction types without code changes

### Type-Safe Access to Events

We can preserve type safety when accessing event data:

```fsharp
// Type-safe access functions
module EventAccessors =
    let getPageId = function
        | PageViewed(pageId, _, _) -> Some pageId
        | _ -> None
        
    let getElementId = function
        | ElementInteraction(elementId, _, _, _) -> Some elementId
        | _ -> None
        
    let getFormData = function
        | FormSubmitted(_, formData, _, _) -> Some formData
        | _ -> None
        
    let getTimestamp = function
        | PageViewed(_, _, ts) -> ts
        | ElementInteraction(_, _, _, ts) -> ts
        | FormSubmitted(_, _, _, ts) -> ts
        | SystemEvent(_, _, _, ts) -> ts
```

## Automating Telemetry with Frontmatter

### Frontmatter Definition

We can use frontmatter in FlightDeck pages to define telemetry requirements:

```yaml
---
title: Product Features
description: Explore our product features
telemetry:
  trackPageView: true
  trackElements:
    - selector: "#signup-button"
      id: "signup-button"
      interaction: "click"
    - selector: ".feature-section"
      id: "feature-section"
      interaction: "visible"
  trackForms:
    - selector: "#contact-form"
      id: "contact-form"
---
```

### Telemetry Generation

From this frontmatter, we can automatically generate the necessary code to track events:

```fsharp
// Automatic telemetry generation from frontmatter
let generateTelemetry (page: FlightDeckPage) =
    let telemetry = page.Frontmatter.["telemetry"] :?> Map<string, obj>
    
    let trackPageView =
        if telemetry.ContainsKey("trackPageView") && (telemetry.["trackPageView"] :?> bool) then
            sprintf """
            // Track page view
            eventStore.RecordEvent(UserEvent.PageViewed("%s", Map.empty, DateTime.UtcNow))
            """ page.Id
        else ""
    
    let trackElements =
        if telemetry.ContainsKey("trackElements") then
            let elements = telemetry.["trackElements"] :?> list<Map<string, string>>
            elements
            |> List.map (fun element ->
                sprintf """
                // Track element interaction
                document.querySelector("%s").addEventListener("%s", function() {
                    eventStore.RecordEvent(UserEvent.ElementInteraction("%s", "%s", Map.empty, DateTime.UtcNow))
                });
                """ element.["selector"] element.["interaction"] element.["id"] element.["interaction"]
            )
            |> String.concat "\n"
        else ""
    
    trackPageView + "\n" + trackElements
```

## Event Storage Model

To support this flexible event model in Marten:

```fsharp
// Configure Marten to store our generic events
let configureMartenStorage (options: StoreOptions) =
    options.Events.AddEventType<UserEvent>()
    
    // Create a custom serializer for our events
    options.Serializer(
        SerializerFactory.For<Newtonsoft.Json.JsonSerializerSettings>(
            settings => {
                settings.TypeNameHandling = TypeNameHandling.Auto;
                settings.Converters.Add(new UserEventJsonConverter());
            }
        )
    )
```

## Migration from Existing Code

For projects migrating from a CollabGateway-style implementation:

```fsharp
// Helper to migrate from old event types
let migrateFromLegacyEvents (legacyEvent: obj) =
    match legacyEvent with
    | :? ButtonEventCase as buttonEvent ->
        match buttonEvent with
        | HomeButtonClicked e ->
            UserEvent.ElementInteraction("home-button", "click", 
                Map.ofList ["page", "home"], e.TimeStamp)
        | DataPolicyAcceptButtonClicked e ->
            UserEvent.ElementInteraction("data-policy-accept", "click", 
                Map.ofList ["policy", "data"], e.TimeStamp)
        // ... other cases
    | :? PageEventCase as pageEvent ->
        match pageEvent with
        | HomePageVisited e ->
            UserEvent.PageViewed("home", Map.empty, e.TimeStamp)
        // ... other cases
    | _ -> 
        UserEvent.SystemEvent("unknown", Map.empty, Map.empty, DateTime.UtcNow)
```

## Conclusion

This streamlined domain model significantly reduces the "DU sprawl" of the original implementation while preserving the benefits of F#'s type system. By using a more generic approach with strong typing around the event structure, we create a flexible system that can adapt to changing requirements without constant code modifications.

The automated telemetry generation from frontmatter provides a clean way to specify tracking requirements directly in content files, freeing developers from boilerplate code. This approach maintains the power of event sourcing while making the system more maintainable and flexible for future growth.