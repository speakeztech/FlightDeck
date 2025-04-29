# Marten and PostgreSQL Integration Guide

## Introduction

This guide outlines the integration of Marten and PostgreSQL with the FlightDeck Oxpecker architecture. We'll focus on leveraging Marten's built-in features rather than implementing custom solutions, emphasizing Oxpecker's advantages over Giraffe.

## Oxpecker Integration with Marten

### Configuration Setup

Oxpecker provides a cleaner way to integrate with Marten compared to Giraffe. Here's how to set up the integration:

```fsharp
open Oxpecker
open Marten
open Microsoft.Extensions.DependencyInjection

let configureServices (services: IServiceCollection) =
    // Configure Marten
    services.AddMarten(fun options ->
        options.Connection("host=localhost;database=flightdeck;username=postgres;password=postgres")
        options.AutoCreateSchemaObjects <- AutoCreate.All
        
        // Register event types
        options.Events.AddEventType<PageViewedEvent>()
        options.Events.AddEventType<ElementInteractionEvent>()
        options.Events.AddEventType<FormSubmittedEvent>()
        
        // Configure projections
        options.Projections.Add<UserActivityProjection>(ProjectionLifecycle.Async)
    )
    |> ignore
    
    // Add Marten daemon for async projections
    services.AddMartenAspNetCoreHosting()
    
let configureApp (app: WebApplication) =
    app.UseOxpecker(endpoints)

// In Program.fs
webHost args {
    add_service configureServices
    app_config configureApp
}
```

### Key Oxpecker Advantages

1. **Task-based HttpHandler**: Oxpecker uses a task-based approach that works naturally with Marten's async API:

```fsharp
// Oxpecker handler for recording events
let recordEvent : HttpHandler =
    fun next ctx -> task {
        let! eventDto = ctx.ReadFromJsonAsync<EventDto>()
        
        // With Oxpecker, we can use task {} directly
        use session = ctx.GetService<IDocumentStore>().LightweightSession()
        
        let streamId = eventDto.StreamId
        let event = mapToEvent eventDto
        
        session.Events.Append(streamId, event)
        do! session.SaveChangesAsync()
        
        return! json { success = true } next ctx
    }
```

2. **Direct Service Access**: Oxpecker provides cleaner access to services in the DI container:

```fsharp
// Oxpecker's simplified service access
let getEvents (streamId: string) : HttpHandler =
    fun next ctx -> task {
        let store = ctx.GetService<IDocumentStore>()
        use session = store.LightweightSession()
        
        let! events = session.Events.FetchStreamAsync(streamId)
        
        return! json events next ctx
    }
```

## Leveraging Marten's Built-in Features

Instead of hand-rolling event processing, use Marten's native capabilities:

### 1. Event Appending

```fsharp
// Simple event recording with Marten
let appendEvent<'T> (streamId: string) (event: 'T) =
    task {
        use session = store.LightweightSession()
        session.Events.Append(streamId, event)
        do! session.SaveChangesAsync()
    }
```

### 2. Stream Aggregation

Use Marten's built-in aggregation instead of custom code:

```fsharp
// Define an aggregate
type UserActivity = {
    UserId: string
    PageViews: int
    LastActive: DateTime
    Forms: Map<string, DateTime>
}

// Configure for inline aggregation
options.Events.InlineProjections.AggregateStreamsWith<UserActivity>()

// Then query directly
let getUserActivity (userId: string) =
    task {
        use session = store.QuerySession()
        let! activity = session.Events.AggregateStreamAsync<UserActivity>(userId)
        return activity
    }
```

### 3. Event Store Metadata

Utilize Marten's event metadata tracking:

```fsharp
// Store event with metadata
let recordWithMetadata (streamId: string) (event: obj) (metadata: Map<string, obj>) =
    task {
        use session = store.LightweightSession()
        
        // Convert to Marten's metadata dictionary
        let metadataDict = 
            metadata 
            |> Map.toSeq 
            |> dict 
            |> Dictionary
            
        session.Events.Append(streamId, event, metadataDict)
        do! session.SaveChangesAsync()
    }
```

## Projections with Marten

Use Marten's projection capabilities instead of hand-rolling projections:

```fsharp
// Implement IProjection for a view model
type UserDashboardProjection() =
    interface IProjection<UserDashboard, string> with
        member _.ProjectEvent(dashboard, eventData, _) =
            let event = eventData.Data
            match event with
            | :? PageViewedEvent as e ->
                let updatedPages = Set.add e.PageId dashboard.PagesVisited
                { dashboard with 
                    PagesVisited = updatedPages
                    LastVisit = e.Timestamp }
                
            | :? FormSubmittedEvent as e ->
                let updatedForms = 
                    dashboard.FormsSubmitted 
                    |> Map.add e.FormId e.Timestamp
                { dashboard with FormsSubmitted = updatedForms }
                
            | _ -> dashboard
            
        member _.DeleteEvent = null
```

## PostgreSQL Integration

### Schema Management

Let Marten handle schema management automatically:

```fsharp
// Automatic schema creation
options.AutoCreateSchemaObjects <- AutoCreate.All

// Or for more control in production
options.AutoCreateSchemaObjects <- AutoCreate.CreateOrUpdate
```

### Transaction Management

Leverage Oxpecker's task-based model with Marten's transactions:

```fsharp
// Transactional handler in Oxpecker
let transactionalUpdate : HttpHandler =
    fun next ctx -> task {
        let! updateDto = ctx.ReadFromJsonAsync<UpdateDto>()
        
        // Get document store from DI
        let store = ctx.GetService<IDocumentStore>()
        
        // Create a transaction session
        use session = store.LightweightSession()
        
        // Record multiple events in a transaction
        session.Events.StartStream<UserProfile>(
            updateDto.UserId,
            ProfileCreatedEvent { Name = updateDto.Name },
            PreferencesUpdatedEvent { Theme = updateDto.Theme }
        )
        
        // Also update a document in the same transaction
        let userSettings = { UserId = updateDto.UserId; Theme = updateDto.Theme }
        session.Store(userSettings)
        
        // Commit everything in one transaction
        do! session.SaveChangesAsync()
        
        return! json { success = true } next ctx
    }
```

## Performance Optimizations

### 1. Batch Operations

Use Marten's batch operations for better performance:

```fsharp
// Batch event append
let recordBatch (events: (string * obj) list) =
    task {
        use session = store.LightweightSession()
        
        for (streamId, event) in events do
            session.Events.Append(streamId, event)
            
        do! session.SaveChangesAsync()
    }
```

### 2. Optimized Queries

Leverage Marten's query capabilities:

```fsharp
// Efficient event querying
let getRecentEvents (streamId: string) (since: DateTime) =
    task {
        use session = store.QuerySession()
        
        let! events = 
            session.Events.QueryAsync<UserEvent>(
                fun x -> 
                    x.StreamId = streamId && 
                    x.Timestamp >= since)
                
        return events
    }
```

## Conclusion

By leveraging Oxpecker's task-based model and Marten's built-in features, you can create a clean, efficient event sourcing system with minimal custom code. This approach reduces boilerplate while maintaining the flexibility and power of event sourcing.

The key advantages of this approach are:
1. Reduced custom code through Marten's native capabilities
2. Cleaner integration through Oxpecker's task-based handlers
3. Better performance through Marten's optimized PostgreSQL operations
4. Simplified maintenance with automatic schema management