# Event Archiving Strategies for SQLite Event Sourcing

Event sourcing systems naturally accumulate large numbers of events over time. While this historical record is valuable, it can lead to performance issues and operational complexity. This document explores strategies for implementing event archiving in SQLite-based event sourcing systems, with specific considerations for FlightDeck.

## Why Consider Event Archiving?

Event archiving addresses several challenges inherent to event sourcing:

1. **Performance Optimization**: Queries against a smaller set of "active" events execute faster
2. **Storage Efficiency**: Reduces the primary database size while preserving historical data
3. **Query Complexity Management**: Simplifies common queries that only need recent events
4. **Backup and Maintenance**: Makes routine database operations more efficient
5. **System Scalability**: Extends the practical lifespan of SQLite for event sourcing

## Archiving Architecture Options

### Option 1: Two-Table Approach (Similar to Marten)

This approach maintains separate tables for current and archived events:

```sql
-- Current events (frequently accessed)
CREATE TABLE IF NOT EXISTS events (
    id TEXT PRIMARY KEY,
    stream_id TEXT NOT NULL,
    version INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    data TEXT NOT NULL, -- JSON
    metadata TEXT NOT NULL, -- JSON
    timestamp TEXT NOT NULL,
    FOREIGN KEY (stream_id) REFERENCES streams(stream_id),
    UNIQUE(stream_id, version)
);

-- Archived events (historical record)
CREATE TABLE IF NOT EXISTS archived_events (
    id TEXT PRIMARY KEY,
    stream_id TEXT NOT NULL,
    version INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    data TEXT NOT NULL, -- JSON
    metadata TEXT NOT NULL, -- JSON
    timestamp TEXT NOT NULL,
    archived_at TEXT NOT NULL
);
```

### Option 2: Single Table with Archive Flag

A simpler approach that uses a single table with an archive flag:

```sql
CREATE TABLE IF NOT EXISTS events (
    id TEXT PRIMARY KEY,
    stream_id TEXT NOT NULL,
    version INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    data TEXT NOT NULL, -- JSON
    metadata TEXT NOT NULL, -- JSON
    timestamp TEXT NOT NULL,
    is_archived INTEGER DEFAULT 0, -- Boolean flag (0=active, 1=archived)
    archived_at TEXT,
    FOREIGN KEY (stream_id) REFERENCES streams(stream_id),
    UNIQUE(stream_id, version)
);

-- Create indexes for efficient querying
CREATE INDEX IF NOT EXISTS idx_events_stream_active ON events(stream_id) WHERE is_archived = 0;
CREATE INDEX IF NOT EXISTS idx_events_stream_all ON events(stream_id, version);
```

### Option 3: Separate Database Files

SQLite allows using attached databases, enabling a more physical separation:

```fsharp
// Attach archive database
let attachArchiveDb (conn: SQLiteConnection) (archivePath: string) =
    let sql = "ATTACH DATABASE @path AS archive"
    Db.newCommand sql
    |> Db.setParams [ "@path", SqlType.String archivePath ]
    |> Db.execNonQuery conn
    |> ignore

// Move events to archive database
let archiveEvents (conn: SQLiteConnection) (streamId: string) (maxVersion: int) =
    // This transaction spans both the main and archive databases
    use transaction = conn.BeginTransaction()
    
    try
        let sql = """
        INSERT INTO archive.events 
        SELECT * FROM main.events
        WHERE stream_id = @streamId AND version <= @version
        """
        
        Db.newCommand sql
        |> Db.setParams [ 
            "@streamId", SqlType.String streamId
            "@version", SqlType.Int maxVersion 
        ]
        |> Db.execNonQuery conn
        |> ignore
        
        // Delete from main after successful archiving
        let deleteSql = """
        DELETE FROM main.events
        WHERE stream_id = @streamId AND version <= @version
        """
        
        Db.newCommand deleteSql
        |> Db.setParams [ 
            "@streamId", SqlType.String streamId
            "@version", SqlType.Int maxVersion 
        ]
        |> Db.execNonQuery conn
        |> ignore
        
        transaction.Commit()
        Ok()
    with
    | ex -> 
        transaction.Rollback()
        Error ex.Message
```

## Archiving Strategies and Policies

### When to Archive

There are several approaches to determine when events should be archived:

1. **Version-Based Archiving**: Archive events older than a specific version threshold
   ```fsharp
   // Archive events when a stream exceeds 100 versions, keeping only the most recent 50
   let archiveByVersionThreshold (conn: SQLiteConnection) =
       let sql = """
       SELECT stream_id, MAX(version) as max_version
       FROM events
       GROUP BY stream_id
       HAVING max_version > 100
       """
       
       let streamsToArchive = 
           Db.newCommand sql
           |> Db.noParams
           |> Db.query conn (fun row -> 
               (row.string "stream_id", row.int "max_version" - 50))
           |> Seq.toList
       
       // Archive each qualifying stream
       for (streamId, thresholdVersion) in streamsToArchive do
           archiveEventsBeforeVersion conn streamId thresholdVersion
           |> ignore
   ```

2. **Time-Based Archiving**: Archive events older than a specific age
   ```fsharp
   // Archive events older than 90 days
   let archiveByAge (conn: SQLiteConnection) (days: int) =
       let cutoffDate = DateTimeOffset.UtcNow.AddDays(float -days)
       let cutoffStr = cutoffDate.ToString("o")
       
       let sql = """
       SELECT DISTINCT stream_id
       FROM events
       WHERE timestamp < @cutoff
       """
       
       let streamsWithOldEvents = 
           Db.newCommand sql
           |> Db.setParams [ "@cutoff", SqlType.String cutoffStr ]
           |> Db.query conn (fun row -> row.string "stream_id")
           |> Seq.toList
       
       // For each stream, find the highest version below the cutoff date
       for streamId in streamsWithOldEvents do
           let versionSql = """
           SELECT MAX(version) as threshold_version
           FROM events
           WHERE stream_id = @streamId AND timestamp < @cutoff
           """
           
           let thresholdVersion = 
               Db.newCommand versionSql
               |> Db.setParams [ 
                   "@streamId", SqlType.String streamId
                   "@cutoff", SqlType.String cutoffStr 
               ]
               |> Db.querySingle conn
               |> Option.map (fun row -> row.int "threshold_version")
               |> Option.defaultValue 0
           
           if thresholdVersion > 0 then
               archiveEventsBeforeVersion conn streamId thresholdVersion
               |> ignore
   ```

3. **State-Based Archiving**: Archive events based on aggregate state
   ```fsharp
   // Archive events for completed presentations
   let archiveCompletedPresentations (conn: SQLiteConnection) =
       let sql = """
       SELECT stream_id
       FROM streams
       WHERE type = 'Presentation' AND 
             EXISTS (
                 SELECT 1 FROM events 
                 WHERE events.stream_id = streams.stream_id AND
                       json_extract(data, '$.Case') = 'PresentationStatusChanged' AND
                       json_extract(data, '$.Fields[0].Status') = 'Published'
             )
       """
       
       let publishedPresentations = 
           Db.newCommand sql
           |> Db.noParams
           |> Db.query conn (fun row -> row.string "stream_id")
           |> Seq.toList
       
       // For published presentations, keep only the most recent 20 events
       for streamId in publishedPresentations do
           let versionSql = """
           SELECT MAX(version) - 20 as archive_threshold
           FROM events
           WHERE stream_id = @streamId
           """
           
           let thresholdVersion = 
               Db.newCommand versionSql
               |> Db.setParams [ "@streamId", SqlType.String streamId ]
               |> Db.querySingle conn
               |> Option.map (fun row -> row.int "archive_threshold")
               |> Option.defaultValue 0
           
           if thresholdVersion > 0 then
               archiveEventsBeforeVersion conn streamId thresholdVersion
               |> ignore
   ```

## Snapshots: A Complementary Strategy

Snapshots provide a performance optimization that works well with archiving:

```sql
CREATE TABLE IF NOT EXISTS snapshots (
    stream_id TEXT PRIMARY KEY,
    version INTEGER NOT NULL,
    data TEXT NOT NULL, -- JSON serialized aggregate state
    timestamp TEXT NOT NULL
);
```

```fsharp
// Create a snapshot of the current state
let createSnapshot<'T> (conn: SQLiteConnection) (streamId: string) (events: Event<'T> list) =
    // Skip if no events
    if List.isEmpty events then Ok()
    else
        // Apply events to reconstruct current state
        let state = applyEvents events
        let latestVersion = events |> List.map (fun e -> e.Version) |> List.max
        let latestTimestamp = events |> List.map (fun e -> e.Metadata.Timestamp) |> List.max
        
        // Serialize the state
        let serializedState = JsonSerializer.Serialize(state)
        
        // Store the snapshot
        let sql = """
        INSERT OR REPLACE INTO snapshots (stream_id, version, data, timestamp)
        VALUES (@streamId, @version, @data, @timestamp)
        """
        
        let parameters = [
            "@streamId", SqlType.String streamId
            "@version", SqlType.Int latestVersion
            "@data", SqlType.String serializedState
            "@timestamp", SqlType.String (latestTimestamp.ToString("o"))
        ]
        
        try
            Db.newCommand sql
            |> Db.setParams parameters
            |> Db.execNonQuery conn
            |> ignore
            Ok()
        with
        | ex -> Error ex.Message
```

## Querying Approaches with Archived Events

### Getting Current State (Fast Path)

```fsharp
// Get the current state using snapshot if available, falling back to events
let getCurrentState<'T> (conn: SQLiteConnection) (streamId: string) =
    // Try to get snapshot
    let snapshotSql = """
    SELECT version, data, timestamp 
    FROM snapshots
    WHERE stream_id = @streamId
    """
    
    let snapshot = 
        Db.newCommand snapshotSql
        |> Db.setParams [ "@streamId", SqlType.String streamId ]
        |> Db.querySingle conn
        |> Option.map (fun row -> 
            {| 
                Version = row.int "version"
                State = JsonSerializer.Deserialize<'T>(row.string "data")
                Timestamp = DateTimeOffset.Parse(row.string "timestamp")
            |})
    
    match snapshot with
    | Some snapshot ->
        // Get only events after the snapshot version
        let recentEvents = getEventsAfterVersion<'T> conn streamId snapshot.Version
        
        if List.isEmpty recentEvents then
            // Return snapshot state directly if no newer events
            Ok (snapshot.State, snapshot.Version)
        else
            // Apply newer events to snapshot state
            let finalState = applyEventsToState snapshot.State recentEvents
            let latestVersion = recentEvents |> List.map (fun e -> e.Version) |> List.max
            Ok (finalState, latestVersion)
            
    | None ->
        // No snapshot, get all active events
        let events = getEvents<'T> conn streamId
        
        if List.isEmpty events then
            // Stream doesn't exist or has no events
            Error "Stream not found or has no events"
        else
            let state = applyEvents events
            let latestVersion = events |> List.map (fun e -> e.Version) |> List.max
            Ok (state, latestVersion)
```

### Querying Historical State (Including Archives)

```fsharp
// Get state at a specific point in time, including archived events
let getStateAtTime<'T> (conn: SQLiteConnection) (streamId: string) (timestamp: DateTimeOffset) =
    // Query for events up to the timestamp
    let sql = """
    SELECT id, stream_id, version, event_type, data, metadata, timestamp
    FROM (
        SELECT * FROM events
        WHERE stream_id = @streamId AND timestamp <= @timestamp
        UNION ALL
        SELECT * FROM archived_events
        WHERE stream_id = @streamId AND timestamp <= @timestamp
    )
    ORDER BY version ASC
    """
    
    let parameters = [
        "@streamId", SqlType.String streamId
        "@timestamp", SqlType.String (timestamp.ToString("o"))
    ]
    
    let events = 
        Db.newCommand sql
        |> Db.setParams parameters
        |> Db.query conn
        |> Seq.map (fun row ->
            let serialized = {
                Id = Guid.Parse(row.string "id")
                StreamId = row.string "stream_id"
                Version = row.int "version"
                EventType = row.string "event_type"
                Data = row.string "data"
                Metadata = row.string "metadata"
                Timestamp = DateTimeOffset.Parse(row.string "timestamp")
            }
            deserializeEvent<'T> serialized)
        |> Seq.toList
    
    if List.isEmpty events then
        Error "No events found for this stream at the specified time"
    else
        let state = applyEvents events
        Ok state
```

## Practical Implementation Recommendations for FlightDeck

1. **Start with Snapshots First**: Implement snapshots before full archiving, as they provide immediate performance benefits.

2. **Choose the Right Architecture**:
   - For simplicity: Single-table approach with archive flag
   - For performance: Two-table approach (similar to Marten)
   - For large-scale: Separate database files

3. **Consider Domain-Specific Archiving Policies**:
   - For presentations: Archive after publication or after a certain period of inactivity
   - For content: Archive based on status transitions (published, deprecated)
   - For user activity: Archive based on time (events older than X months)

4. **Implement Progressive Archiving**:
   - Start with manual archiving capabilities
   - Add automated archiving for specific high-volume streams
   - Eventually implement scheduled archiving jobs

5. **Monitor Performance Metrics**:
   - Track query times for common operations
   - Monitor stream sizes (events per stream)
   - Set thresholds for automatic archiving based on actual usage patterns

## Code Sample: Implementing a Complete Archiving Solution

Here's a sample implementation that ties these concepts together:

```fsharp
module FlightDeck.EventSourcing.Archiving

open System
open System.Data.SQLite
open Donald
open FlightDeck.EventSourcing.Domain

// Archive events before a specific version
let archiveEventsBeforeVersion (conn: SQLiteConnection) (streamId: string) (version: int) =
    use transaction = conn.BeginTransaction()
    
    try
        // First, ensure we have a snapshot that covers these events
        let events = getEvents<obj> conn streamId
                     |> List.filter (fun e -> e.Version <= version)
        
        if not (List.isEmpty events) then
            // Create/update snapshot
            createSnapshot conn streamId events |> ignore
            
            // Move events to archive
            let archiveSql = """
            INSERT INTO archived_events 
                (id, stream_id, version, event_type, data, metadata, timestamp, archived_at)
            SELECT 
                id, stream_id, version, event_type, data, metadata, timestamp, @archivedAt
            FROM events
            WHERE stream_id = @streamId AND version <= @version
            """
            
            let archiveParams = [
                "@streamId", SqlType.String streamId
                "@version", SqlType.Int version
                "@archivedAt", SqlType.String (DateTimeOffset.UtcNow.ToString("o"))
            ]
            
            Db.newCommand archiveSql
            |> Db.setParams archiveParams
            |> Db.execNonQuery conn
            |> ignore
            
            // Delete from main table
            let deleteSql = """
            DELETE FROM events
            WHERE stream_id = @streamId AND version <= @version
            """
            
            Db.newCommand deleteSql
            |> Db.setParams [
                "@streamId", SqlType.String streamId
                "@version", SqlType.Int version
            ]
            |> Db.execNonQuery conn
            |> ignore
        
        transaction.Commit()
        Ok ()
    with
    | ex ->
        transaction.Rollback()
        Error ex.Message

// Archive streams that match specific criteria
let archiveCompletedStreams (conn: SQLiteConnection) =
    // Get streams that have a status-changed event to "Completed"
    let sql = """
    SELECT DISTINCT e1.stream_id, MAX(e1.version) as current_version
    FROM events e1
    JOIN events e2 ON e1.stream_id = e2.stream_id
    WHERE json_extract(e2.data, '$.Case') = 'StatusChanged'
      AND json_extract(e2.data, '$.Fields[0].NewStatus') = 'Completed'
    GROUP BY e1.stream_id
    """
    
    let completedStreams = 
        Db.newCommand sql
        |> Db.noParams
        |> Db.query conn (fun row -> 
            (row.string "stream_id", row.int "current_version"))
        |> Seq.toList
    
    // For each completed stream, archive all but the most recent events
    for (streamId, currentVersion) in completedStreams do
        // Keep the most recent 10 events, archive the rest
        let versionToKeep = max 10 (currentVersion - 10)
        let versionToArchive = versionToKeep - 1
        
        if versionToArchive > 0 then
            archiveEventsBeforeVersion conn streamId versionToArchive
            |> ignore

// Automated archive job
let runArchiveJob (dbPath: string) =
    let connString = sprintf "Data Source=%s;Version=3;" dbPath
    use conn = new SQLiteConnection(connString)
    conn.Open()
    
    // Archive completed streams
    archiveCompletedStreams conn
    
    // Archive old events (older than 90 days)
    archiveByAge conn 90
    
    // Archive streams with too many versions
    archiveByVersionThreshold conn
```

## Conclusion

Implementing archiving in SQLite event sourcing enables FlightDeck to maintain performance as event volumes grow. The right approach depends on your specific needs, but the combination of snapshots and archiving provides an effective solution for managing event data over the long term.

Start with a minimal implementation focusing on snapshots and manual archiving capabilities, then evolve the strategy based on actual usage patterns and performance metrics. This approach gives you the benefits of event sourcing without the operational challenges of ever-growing event tables.