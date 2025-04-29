# Event Projections and Read Models

## Introduction

This document outlines best practices for creating and managing projections in Marten to transform events into useful read models within the FlightDeck Oxpecker architecture. The focus is on leveraging Marten's built-in projection system rather than hand-rolling custom projection code.

## Marten Projection Types

Marten offers several types of projections, each with specific use cases:

### 1. Inline Projections

Ideal for simple aggregates that follow the event stream:

```fsharp
// Configure inline projections
let configureInlineProjections (options: StoreOptions) =
    // Aggregate user activity directly from events
    options.Events.InlineProjections.AggregateStreamsWith<UserActivity>()
    
    // Transform events to another format
    options.Events.InlineProjections.TransformEvents(
        fun (event: PageViewedEvent) -> 
            PageViewRecord(event.PageId, event.Timestamp, event.UserId)
    )
```

Usage in Oxpecker handlers:

```fsharp
// Get aggregated activity
let getUserActivity (userId: string) : HttpHandler =
    fun next ctx -> task {
        let store = ctx.GetService<IDocumentStore>()
        use session = store.QuerySession()
        
        // Automatically aggregated from events
        let! activity = session.Events.AggregateStreamAsync<UserActivity>(userId)
        
        return! json activity next ctx
    }
```

### 2. Live Aggregation

For on-demand aggregation of events:

```fsharp
// Define an aggregate function
let aggregateUserProfile (events: IReadOnlyList<IEvent>) =
    events
    |> Seq.fold (fun (profile: UserProfile option) event ->
        match event.Data with
        | :? ProfileCreatedEvent as e ->
            Some { UserId = e.UserId; Name = e.Name; Email = e.Email }
        | :? ProfileUpdatedEvent as e ->
            profile |> Option.map (fun p -> { p with Name = e.Name })
        | _ -> profile
    ) None
    |> Option.defaultValue { UserId = ""; Name = ""; Email = "" }

// Use in a handler
let getUserProfile (userId: string) : HttpHandler =
    fun next ctx -> task {
        let store = ctx.GetService<IDocumentStore>()
        use session = store.QuerySession()
        
        let! events = session.Events.FetchStreamAsync(userId)
        let profile = aggregateUserProfile events
        
        return! json profile next ctx
    }
```

### 3. Async Projections

The most powerful option for complex projections that update automatically:

```fsharp
// Define a projection class
type UserDashboardProjection() =
    interface IProjection<UserDashboard, string> with
        member _.ProjectEvent(dashboard, eventData, _) =
            match eventData.Data with
            | :? PageViewedEvent as e ->
                let pageViews = dashboard.PageViews + 1
                let pageVisits = 
                    dashboard.PageVisits 
                    |> Map.change e.PageId (function 
                        | Some count -> Some (count + 1)
                        | None -> Some 1)
                        
                { dashboard with 
                    PageViews = pageViews
                    PageVisits = pageVisits
                    LastActive = e.Timestamp }
                
            | :? FormSubmittedEvent as e ->
                let formSubmissions = dashboard.FormSubmissions + 1
                
                { dashboard with 
                    FormSubmissions = formSubmissions
                    LastFormSubmitted = Some e.FormId
                    LastActive = e.Timestamp }
                
            | _ -> dashboard
        
        // Identify which stream this document belongs to
        member _.Identity(eventData) =
            match eventData.Data with
            | :? PageViewedEvent as e -> e.UserId
            | :? FormSubmittedEvent as e -> e.UserId
            | _ -> eventData.StreamId
```

Configure the async daemon to run these projections:

```fsharp
// Register the projection
let configureAsyncProjections (services: IServiceCollection) =
    services.AddMarten()
        .AddAsyncDaemon(DaemonMode.HotCold)
        .AddProjection<UserDashboardProjection>()
```

## Multi-Stream Projections

Create projections that aggregate data across multiple streams:

```fsharp
// Site-wide analytics projection
type SiteAnalyticsProjection() =
    interface IProjection<SiteAnalytics, string> with
        member _.ProjectEvent(analytics, eventData, _) =
            match eventData.Data with
            | :? PageViewedEvent as e ->
                let totalViews = analytics.TotalPageViews + 1
                let uniqueVisitors = 
                    analytics.UniqueVisitors |> Set.add e.UserId
                let pageViews = 
                    analytics.PageViews 
                    |> Map.change e.PageId (function 
                        | Some count -> Some (count + 1)
                        | None -> Some 1)
                        
                { analytics with 
                    TotalPageViews = totalViews
                    UniqueVisitors = uniqueVisitors
                    PageViews = pageViews }
                
            | _ -> analytics
        
        // Always project to a single document
        member _.Identity(_) = "site_analytics"
```

## Efficient Querying of Read Models

### 1. Custom Indexes

Add indexes for frequently queried properties:

```fsharp
// Configure document indexes
let configureIndexes (options: StoreOptions) =
    options.Schema.For<UserDashboard>()
        .Index(fun x -> x.LastActive)
        .Index(fun x -> x.PageViews)
```

### 2. Optimized Queries

Use Marten's query API with Oxpecker:

```fsharp
// Get most active users for the past week
let getActiveUsers : HttpHandler =
    fun next ctx -> task {
        let store = ctx.GetService<IDocumentStore>()
        use session = store.QuerySession()
        
        let oneWeekAgo = DateTime.UtcNow.AddDays(-7)
        
        let! activeUsers = 
            session.Query<UserDashboard>()
                .Where(fun x -> x.LastActive >= oneWeekAgo)
                .OrderByDescending(fun x -> x.PageViews)
                .Take(10)
                .ToListAsync()
                
        return! json activeUsers next ctx
    }
```

## Event-Sourced Views with Time Travel

Leverage Marten's event tracking for point-in-time reconstruction:

```fsharp
// Get site analytics at a specific point in time
let getHistoricalAnalytics (timestamp: DateTime) : HttpHandler =
    fun next ctx -> task {
        let store = ctx.GetService<IDocumentStore>()
        use session = store.QuerySession()
        
        // Fetch all events up to the specified timestamp
        let! events = 
            session.Events.QueryForSingleStream<SiteAnalyticsEvent>("site_analytics")
                .Where(fun x -> x.Timestamp <= timestamp)
                .ToListAsync()
        
        // Reconstruct analytics from events
        let analytics = events |> aggregateSiteAnalytics
        
        return! json analytics next ctx
    }
```

## Integration with Oxpecker ViewEngine

Use your read models with Oxpecker's viewEngine:

```fsharp
// Render dashboard view
let dashboardPage (userId: string) : HttpHandler =
    fun next ctx -> task {
        let store = ctx.GetService<IDocumentStore>()
        use session = store.QuerySession()
        
        let! dashboard = session.LoadAsync<UserDashboard>(userId)
        
        let view =
            html [ _lang "en" ] [
                head [] [
                    title [] [ rawText "User Dashboard" ]
                ]
                body [] [
                    h1 [] [ rawText "Dashboard" ]
                    div [] [
                        h2 [] [ rawText "Activity Summary" ]
                        p [] [ rawText $"Total page views: {dashboard.PageViews}" ]
                        p [] [ rawText $"Forms submitted: {dashboard.FormSubmissions}" ]
                    ]
                ]
            ]
            
        return! htmlView view next ctx
    }
```

## Live Updates with WebSockets

Combine projections with WebSocket notifications to create reactive UIs:

```fsharp
// WebSocket handler for live updates
let dashboardUpdates (userId: string) : HttpHandler =
    fun next ctx -> webSocket (fun ws ->
        task {
            // Subscribe to event notifications
            use subscription = eventNotifier.Subscribe(userId)
            
            // When new events come in, fetch updated dashboard
            use! _ = subscription.MessageReceived.AddHandler(fun _ _ ->
                task {
                    let store = ctx.GetService<IDocumentStore>()
                    use session = store.QuerySession()
                    
                    let! dashboard = session.LoadAsync<UserDashboard>(userId)
                    let json = Newtonsoft.Json.JsonConvert.SerializeObject(dashboard)
                    
                    // Send update to client
                    do! ws.SendAsync(
                        ArraySegment(Encoding.UTF8.GetBytes(json)),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None)
                }
            )
            
            // Keep connection open until client disconnects
            let buffer = Array.zeroCreate<byte> 4096
            let mutable receiving = true
            
            while receiving do
                let! result = ws.ReceiveAsync(ArraySegment(buffer), CancellationToken.None)
                receiving <- not result.CloseStatus.HasValue
                
            return! ws.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Closing",
                CancellationToken.None)
        }
    ) next ctx
```

## Best Practices

1. **Separate Read Models from Domain Models**: Keep your read models optimized for specific views.

2. **Use Appropriate Projection Types**:
   - Inline projections for simple aggregations
   - Async projections for complex, multi-document projections
   - Live aggregation for on-demand, point-in-time views

3. **Design for Performance**:
   - Add indexes on commonly queried fields
   - Use projections to pre-compute expensive calculations
   - Consider data denormalization for read efficiency

4. **Monitor Projection Health**:
   - Track projection progress and errors
   - Implement retry logic for failed projections

## Conclusion

By leveraging Marten's built-in projection capabilities, you can create sophisticated read models without writing custom projection code. This approach reduces maintenance overhead while providing powerful features like async updates, multi-stream aggregation, and point-in-time reconstruction.

The integration with Oxpecker creates a seamless development experience, from event capture to data visualization, all within a type-safe F# environment.