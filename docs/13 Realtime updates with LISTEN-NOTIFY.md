# Real-time Updates with PostgreSQL LISTEN/NOTIFY

## Introduction

This document explores how to implement real-time updates in FlightDeck using PostgreSQL's built-in LISTEN/NOTIFY mechanism. This approach offers a lightweight, reliable way to deliver immediate updates to clients without polling or maintaining complex messaging infrastructure.

## Understanding PostgreSQL LISTEN/NOTIFY

PostgreSQL's LISTEN/NOTIFY is a pub/sub mechanism that allows processes to communicate asynchronously. Here's how it works:

1. **LISTEN**: A process registers interest in a specific notification channel
2. **NOTIFY**: Another process sends a message on that channel
3. **Notification**: All listening processes receive the message

### Basic PostgreSQL Commands

```sql
-- Process A: Listen for notifications on 'events_channel'
LISTEN events_channel;

-- Process B: Send a notification
NOTIFY events_channel, 'New event occurred';

-- Process A receives the notification asynchronously
```

## Why Use LISTEN/NOTIFY with Event Sourcing?

Event sourcing systems naturally generate notifications when events occur. PostgreSQL's LISTEN/NOTIFY offers several advantages:

1. **Built-in to PostgreSQL**: No additional messaging systems needed
2. **Transactional**: Notifications can be part of the same transaction as event storage
3. **Low Latency**: Near-immediate delivery for connected clients
4. **Simple Implementation**: Minimal code required

## Implementation in FlightDeck

### 1. Event Bus Integration

Create an event bus that leverages LISTEN/NOTIFY:

```fsharp
/// Event bus implementation using Postgres LISTEN/NOTIFY
type PostgresEventBus(connectionString: string) =
    let mutable connection: NpgsqlConnection = null
    let listeners = ConcurrentDictionary<string, (string -> unit) list>()
    let mutable isRunning = false
    
    /// Start listening for notifications
    member this.Start() =
        if not isRunning then
            isRunning <- true
            
            // Create connection
            connection <- new NpgsqlConnection(connectionString)
            connection.Open()
            
            // Listen for notifications on our channel
            use cmd = connection.CreateCommand()
            cmd.CommandText <- "LISTEN events_channel;"
            cmd.ExecuteNonQuery() |> ignore
            
            // Handle notifications
            connection.Notification.Add(fun args ->
                try
                    // Parse JSON payload
                    let notification = JsonSerializer.Deserialize<EventNotification>(args.Payload)
                    
                    // Find handlers for this event type
                    match listeners.TryGetValue(notification.EventType) with
                    | true, handlers ->
                        // Notify all handlers
                        handlers |> List.iter (fun handler -> 
                            handler notification.Payload)
                    | false, _ -> ()
                with ex ->
                    printfn "Error processing notification: %s" ex.Message
            )
            
            // Keep connection alive
            async {
                while isRunning do
                    if connection.State <> ConnectionState.Open then
                        connection.Open()
                        let cmd = connection.CreateCommand()
                        cmd.CommandText <- "LISTEN events_channel;"
                        cmd.ExecuteNonQuery() |> ignore
                        
                    do! Async.Sleep(30000) // 30 second heartbeat
            } |> Async.Start
    
    /// Subscribe to notifications
    member this.Subscribe(eventType: string, handler: string -> unit) =
        listeners.AddOrUpdate(
            eventType,
            [handler],
            fun _ currentHandlers -> handler :: currentHandlers)
        |> ignore
    
    /// Unsubscribe from notifications
    member this.Unsubscribe(eventType: string, handler: string -> unit) =
        listeners.AddOrUpdate(
            eventType,
            [],
            fun _ currentHandlers -> 
                currentHandlers |> List.filter (fun h -> h <> handler))
        |> ignore
    
    /// Publish an event notification
    member this.Publish(eventType: string, payload: string) =
        async {
            use conn = new NpgsqlConnection(connectionString)
            do! conn.OpenAsync() |> Async.AwaitTask
            
            // Create notification with event type and payload
            let notification = {
                EventType = eventType
                Payload = payload
                Timestamp = DateTimeOffset.UtcNow
            }
            
            let notificationJson = JsonSerializer.Serialize(notification)
            
            // Send notification via NOTIFY
            use cmd = new NpgsqlCommand(
                "SELECT pg_notify('events_channel', @payload)",
                conn)
            cmd.Parameters.AddWithValue("payload", notificationJson) |> ignore
            
            do! cmd.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
        } |> Async.Start
```

### 2. Integration with Marten

Connect the event bus to Marten's event store:

```fsharp
/// Register with Marten's event store
let configureEventPublisher (store: IDocumentStore) (eventBus: PostgresEventBus) =
    // Subscribe to Marten's event stream
    store.Events.AfterCommitAppend.Subscribe(fun events ->
        // Process each event
        for streamEvent in events.Documents do
            let eventData = streamEvent.Data
            
            // Determine event type and serialize payload
            let eventType, payload =
                match eventData with
                | :? PageViewedEvent as e -> 
                    "PageViewed", JsonSerializer.Serialize(e)
                | :? ElementInteractionEvent as e -> 
                    "ElementInteraction", JsonSerializer.Serialize(e)
                | :? FormSubmittedEvent as e -> 
                    "FormSubmitted", JsonSerializer.Serialize(e)
                | _ -> "", ""
                
            // Publish event if recognized
            if eventType <> "" then
                eventBus.Publish(eventType, payload)
    )
```

### 3. Oxpecker Server-Sent Events (SSE) Integration

Create an SSE endpoint to stream events to browsers:

```fsharp
/// Server-Sent Events handler for streaming events
let eventStreamHandler (eventBus: PostgresEventBus) : HttpHandler =
    fun next ctx -> task {
        // Set headers for SSE
        ctx.Response.ContentType <- "text/event-stream"
        ctx.Response.Headers.Append("Cache-Control", "no-cache")
        ctx.Response.Headers.Append("Connection", "keep-alive")
        
        // Create cancellation token for client disconnect
        let cts = new CancellationTokenSource()
        
        // Register client disconnect
        ctx.HttpContext.RequestAborted.Register(fun () -> 
            cts.Cancel()
        ) |> ignore
        
        // Event writing function
        let writeEvent (payload: string) =
            if not cts.IsCancellationRequested then
                // Format as SSE
                let data = sprintf "data: %s\n\n" payload
                
                // Write to response
                ctx.HttpContext.Response.WriteAsync(data, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult()
                    
                ctx.HttpContext.Response.Body.FlushAsync(cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult()
        
        // Subscribe to events
        eventBus.Subscribe("PageViewed", writeEvent)
        eventBus.Subscribe("ElementInteraction", writeEvent)
        eventBus.Subscribe("FormSubmitted", writeEvent)
        
        // Keep connection open until client disconnects
        try
            let tcs = TaskCompletionSource()
            ctx.HttpContext.RequestAborted.Register(fun () -> tcs.SetResult()) |> ignore
            do! tcs.Task
            return! next ctx
        finally
            // Clean up subscriptions
            eventBus.Unsubscribe("PageViewed", writeEvent)
            eventBus.Unsubscribe("ElementInteraction", writeEvent)
            eventBus.Unsubscribe("FormSubmitted", writeEvent)
    }
```

### 4. WebSocket Implementation

Alternative WebSocket integration for bidirectional communication:

```fsharp
/// WebSocket handler for real-time updates
let webSocketHandler (eventBus: PostgresEventBus) : HttpHandler =
    fun next ctx -> webSocket (fun ws -> 
        task {
            // Buffer for receiving messages
            let buffer = Array.zeroCreate<byte> 4096
            let mutable receiving = true
            
            // Handle client subscriptions
            let subscriptions = ResizeArray<string * (string -> unit)>()
            
            // Process messages from client
            while receiving do
                let! result = ws.ReceiveAsync(ArraySegment(buffer), CancellationToken.None)
                
                if result.MessageType = WebSocketMessageType.Text then
                    // Extract message content
                    let message = 
                        Encoding.UTF8.GetString(buffer, 0, result.Count)
                    
                    // Parse subscription request
                    try
                        let request = JsonSerializer.Deserialize<SubscriptionRequest>(message)
                        
                        // Create event handler
                        let handler = fun payload ->
                            task {
                                // Send notification to client
                                do! ws.SendAsync(
                                    ArraySegment(Encoding.UTF8.GetBytes(payload)),
                                    WebSocketMessageType.Text,
                                    true,
                                    CancellationToken.None)
                            } |> ignore
                        
                        // Subscribe to event type
                        eventBus.Subscribe(request.EventType, handler)
                        
                        // Track subscription for cleanup
                        subscriptions.Add((request.EventType, handler))
                    with ex ->
                        printfn "Error processing subscription: %s" ex.Message
                else if result.CloseStatus.HasValue then
                    receiving <- false
            
            // Clean up subscriptions
            for (eventType, handler) in subscriptions do
                eventBus.Unsubscribe(eventType, handler)
                
            // Close WebSocket
            return! ws.CloseAsync(
                (result.CloseStatus.GetValueOrDefault(WebSocketCloseStatus.NormalClosure)),
                (result.CloseStatusDescription),
                CancellationToken.None)
        }) next ctx
```

### 5. Client-Side Integration

JavaScript code to consume the SSE stream:

```javascript
// Connect to the event stream
const eventSource = new EventSource('/api/events');

// Listen for events
eventSource.onmessage = (event) => {
    try {
        const eventData = JSON.parse(event.data);
        
        // Handle different event types
        switch (eventData.eventType) {
            case 'PageViewed':
                updatePageViews(eventData);
                break;
            case 'ElementInteraction':
                updateInteractions(eventData);
                break;
            case 'FormSubmitted':
                updateFormSubmissions(eventData);
                break;
        }
    } catch (err) {
        console.error('Error processing event', err);
    }
};

// Handle connection errors
eventSource.onerror = (error) => {
    console.error('EventSource failed:', error);
    // Reconnect after delay
    setTimeout(() => {
        eventSource.close();
        // Reconnect logic
    }, 5000);
};
```

### 6. Configuration in Oxpecker

Register the event bus in the Oxpecker application:

```fsharp
// Services configuration
let configureServices (services: IServiceCollection) =
    // Add event bus
    services.AddSingleton<PostgresEventBus>(fun provider -> 
        let connectionString = provider.GetService<IConfiguration>().GetConnectionString("Marten")
        let bus = new PostgresEventBus(connectionString)
        bus.Start()
        bus
    )
    
    // Add Marten with event store
    services.AddMarten(fun options ->
        options.Connection(connectionString)
        // Event store configuration
    )
    
    // Configure event publisher
    services.AddHostedService<EventPublisherService>()

// Configure endpoints
let endpoints = [
    GET [
        route "/api/events" >=> eventStreamHandler
        route "/ws/events" >=> webSocketHandler
    ]
]
```

## Advanced Techniques

### 1. Filtered Notifications

Subscribe to specific event patterns:

```fsharp
// Subscribe to page views for a specific page
eventBus.Subscribe("PageViewed", fun payload ->
    let pageEvent = JsonSerializer.Deserialize<PageViewedEvent>(payload)
    if pageEvent.PageId = "home" then
        // Process home page views only
        processHomePageView(pageEvent)
)
```

### 2. Multi-Channel Notifications

Use multiple notification channels for better organization:

```fsharp
// Listen on multiple channels
connection.Notification.Add(fun args ->
    // Process based on channel
    match args.Channel with
    | "user_events" -> processUserEvent(args.Payload)
    | "system_events" -> processSystemEvent(args.Payload)
    | _ -> ()
)
```

### 3. Transaction Integration

Ensure notifications are part of the same transaction as the event storage:

```fsharp
// Execute within a transaction
use tx = connection.BeginTransaction()

// Store the event
use insertCmd = connection.CreateCommand()
insertCmd.Transaction <- tx
insertCmd.CommandText <- "INSERT INTO events (...) VALUES (...);"
insertCmd.ExecuteNonQuery() |> ignore

// Send notification within the same transaction
use notifyCmd = connection.CreateCommand()
notifyCmd.Transaction <- tx
notifyCmd.CommandText <- "NOTIFY events_channel, @payload;"
notifyCmd.Parameters.AddWithValue("payload", notificationJson) |> ignore
notifyCmd.ExecuteNonQuery() |> ignore

// Commit everything
tx.Commit()
```

## Deployment Considerations

### 1. Connection Pooling

Be mindful of connection usage:

```fsharp
// Configure Npgsql connection pooling
NpgsqlConnectionStringBuilder.MaxPoolSize <- 100
NpgsqlConnectionStringBuilder.MinPoolSize <- 10
```

### 2. Payload Size Limits

PostgreSQL has a 8000 byte limit for notification payloads. For larger data, use a reference pattern:

```fsharp
// Reference pattern for large payloads
let referenceNotification = {
    Type = "EventReference"
    EventId = eventId  // Store only the ID
    Timestamp = DateTime.UtcNow
}

// Client fetches complete data separately
let fetchCompleteEvent (eventId: string) : HttpHandler =
    fun next ctx -> task {
        let store = ctx.GetService<IDocumentStore>()
        let! event = store.Events.Load<EventData>(eventId)
        return! json event next ctx
    }
```

### 3. Reconnection and Recovery

Handle connection issues gracefully:

```fsharp
// Connection management
let rec maintainConnection() =
    async {
        try
            if connection.State <> ConnectionState.Open then
                connection.Open()
                resubscribeToChannels()
        with ex ->
            printfn "Connection error: %s" ex.Message
            do! Async.Sleep(5000)  // Retry after 5 seconds
        
        do! Async.Sleep(30000)  // Check every 30 seconds
        return! maintainConnection()
    }
```

## Performance Optimization

### 1. Filtering at Source

Filter notifications before sending to reduce network traffic:

```fsharp
// Only notify for specific events
match eventData with
| :? PageViewedEvent as e when e.IsPublicPage -> 
    eventBus.Publish("PublicPageViewed", JsonSerializer.Serialize(e))
| :? FormSubmittedEvent as e when e.IsImportant -> 
    eventBus.Publish("ImportantFormSubmitted", JsonSerializer.Serialize(e))
| _ -> 
    // Don't publish events we don't care about
    ()
```

### 2. Batching Notifications

Batch notifications for high-frequency events:

```fsharp
// Notification batcher
type NotificationBatcher() =
    let events = ConcurrentQueue<obj>()
    let timerInterval = 1000 // 1 second
    
    let publishBatch() =
        let batch = ResizeArray<obj>()
        let mutable item = Unchecked.defaultof<obj>
        
        while events.TryDequeue(&item) do
            batch.Add(item)
            
        if batch.Count > 0 then
            eventBus.Publish("BatchedEvents", JsonSerializer.Serialize(batch))
    
    let timer = new Timer(fun _ -> publishBatch(), null, timerInterval, timerInterval)
    
    member _.AddEvent(event: obj) =
        events.Enqueue(event)
```

## Conclusion

PostgreSQL's LISTEN/NOTIFY mechanism provides a powerful, built-in solution for real-time updates in event-sourced applications. By integrating this feature with Marten and Oxpecker, you can create responsive, real-time web applications without the need for additional messaging infrastructure.

This approach is particularly well-suited for FlightDeck's architecture, offering a clean, simple way to notify clients of changes while maintaining the benefits of event sourcing. The implementation is lightweight, performs well at scale, and integrates seamlessly with both server and client components.