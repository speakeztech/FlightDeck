# SQLite JSON Queries with Donald in F#

## Introduction

This document explains how to adapt PostgreSQL-style JSON queries to SQLite when working with an event sourcing system. While PostgreSQL uses the `jsonb` type and operators like `@>`, SQLite provides its own set of JSON functions that we'll use with Donald (an F# ADO.NET wrapper) to query complex event data.

## JSON Functions in SQLite vs PostgreSQL

| PostgreSQL | SQLite | Description |
|------------|--------|-------------|
| `data::jsonb @> '{"key": "value"}'` | `json_extract(data, '$.key') = 'value'` | Test if JSON contains a key-value pair |
| `data->'key'` | `json_extract(data, '$.key')` | Extract a JSON object by key |
| `data->>'key'` | `json_extract(data, '$.key')` | Extract a JSON value as text |
| `data->0` | `json_extract(data, '$[0]')` | Extract array element by index |
| `data->'key'->>'field'` | `json_extract(data, '$.key.field')` | Extract nested fields |

## Event Query Implementation with Donald and SQLite

```fsharp
module FlightDeck.EventSourcing.Queries

open System
open System.Data.SQLite
open Donald

// Define the record type for ASN data results
type ASNData = {
    ASnumber: string
    ISP: string
    ASNtype: string
    StreamCount: int
}

/// Query streams without data policy decisions, grouped by ASN information
let getStreamsWithoutDataPolicyByASN (conn: SQLiteConnection) =
    // This adapts the PostgreSQL query to use SQLite JSON functions
    let sql = """
    WITH streams_with_datapolicy AS (
        SELECT DISTINCT stream_id
        FROM mt_events
        WHERE json_extract(data, '$.Case') = 'DataPolicyAcceptButtonClicked'
           OR json_extract(data, '$.Case') = 'DataPolicyDeclinedButtonClicked'
    ),
    active_streams_without_datapolicy AS (
        SELECT DISTINCT stream_id
        FROM mt_events
        WHERE stream_id NOT IN (SELECT stream_id FROM streams_with_datapolicy)
    )
    SELECT 
        json_extract(data, '$.Fields[0].UserGeoInfo.as.asn') AS ASnumber,
        json_extract(data, '$.Fields[0].UserGeoInfo.isp') AS ISP,
        json_extract(data, '$.Fields[0].UserGeoInfo.as.type') AS ASNtype,
        COUNT(DISTINCT stream_id) AS stream_count
    FROM mt_events
    WHERE stream_id IN (SELECT stream_id FROM active_streams_without_datapolicy)
      AND json_extract(data, '$.Fields[0].UserGeoInfo.as.asn') IS NOT NULL
    GROUP BY ASnumber, ISP, ASNtype
    ORDER BY stream_count DESC;
    """
    
    // Execute the query using Donald's functional API
    Db.newCommand sql
    |> Db.noParams
    |> Db.query conn (fun row ->
        {
            ASnumber = row.string "ASnumber"
            ISP = row.string "ISP"
            ASNtype = row.string "ASNtype"
            StreamCount = row.int "stream_count"
        })
    |> Seq.toList

/// Find streams with a specific ASN but no data policy
let findStreamsWithASNButNoPolicy (conn: SQLiteConnection) (asnNumber: string) =
    let sql = """
    WITH streams_with_datapolicy AS (
        SELECT DISTINCT stream_id
        FROM mt_events
        WHERE json_extract(data, '$.Case') = 'DataPolicyAcceptButtonClicked'
           OR json_extract(data, '$.Case') = 'DataPolicyDeclinedButtonClicked'
    )
    SELECT e.stream_id, 
           json_extract(e.data, '$.Fields[0].UserGeoInfo.country') AS Country,
           json_extract(e.data, '$.Fields[0].UserGeoInfo.city') AS City,
           MAX(e.timestamp) as last_seen
    FROM mt_events e
    WHERE e.stream_id NOT IN (SELECT stream_id FROM streams_with_datapolicy)
      AND json_extract(e.data, '$.Fields[0].UserGeoInfo.as.asn') = @asnNumber
    GROUP BY e.stream_id, Country, City
    ORDER BY last_seen DESC;
    """
    
    // Type-safe parameters using Donald
    let asnParam = [ "@asnNumber", SqlType.String asnNumber ]
    
    // Record type for the results
    type StreamGeoData = {
        StreamId: string
        Country: string option
        City: string option
        LastSeen: DateTimeOffset
    }
    
    // Execute the query with parameters
    Db.newCommand sql
    |> Db.setParams asnParam
    |> Db.query conn (fun row ->
        {
            StreamId = row.string "stream_id"
            Country = row.stringOrNone "Country"
            City = row.stringOrNone "City"
            LastSeen = DateTimeOffset.Parse(row.string "last_seen")
        })
    |> Seq.toList

/// Find content of specific events by JSON path
let findEventsByJsonPath (conn: SQLiteConnection) (jsonPath: string) (value: string) =
    let sql = """
    SELECT id, stream_id, version, event_type, data, metadata, timestamp
    FROM events
    WHERE json_extract(data, @path) = @value
    ORDER BY timestamp DESC
    LIMIT 100
    """
    
    let parameters = [
        "@path", SqlType.String jsonPath
        "@value", SqlType.String value
    ]
    
    // Type for serialized events
    type SerializedEvent = {
        Id: Guid
        StreamId: string
        Version: int
        EventType: string
        Data: string  // JSON serialized
        Metadata: string  // JSON serialized
        Timestamp: DateTimeOffset
    }
    
    Db.newCommand sql
    |> Db.setParams parameters
    |> Db.query conn (fun row ->
        {
            Id = Guid.Parse(row.string "id")
            StreamId = row.string "stream_id"
            Version = row.int "version"
            EventType = row.string "event_type"
            Data = row.string "data"
            Metadata = row.string "metadata"
            Timestamp = DateTimeOffset.Parse(row.string "timestamp")
        })
    |> Seq.toList

/// Complex query to analyze event patterns over time
let analyzeEventPatterns (conn: SQLiteConnection) (eventType: string) (startDate: DateTimeOffset) (endDate: DateTimeOffset) =
    let sql = """
    WITH daily_counts AS (
        SELECT 
            date(timestamp) as event_date,
            COUNT(*) as event_count,
            COUNT(DISTINCT stream_id) as stream_count
        FROM events
        WHERE event_type = @eventType
          AND timestamp BETWEEN @startDate AND @endDate
        GROUP BY event_date
    )
    SELECT 
        event_date,
        event_count,
        stream_count,
        AVG(event_count) OVER (
            ORDER BY event_date
            ROWS BETWEEN 6 PRECEDING AND CURRENT ROW
        ) as rolling_avg_7day
    FROM daily_counts
    ORDER BY event_date;
    """
    
    let parameters = [
        "@eventType", SqlType.String eventType
        "@startDate", SqlType.String (startDate.ToString("o"))
        "@endDate", SqlType.String (endDate.ToString("o"))
    ]
    
    type EventAnalytics = {
        EventDate: DateTime
        EventCount: int
        StreamCount: int
        RollingAvg7Day: float
    }
    
    Db.newCommand sql
    |> Db.setParams parameters
    |> Db.query conn (fun row ->
        {
            EventDate = DateTime.Parse(row.string "event_date")
            EventCount = row.int "event_count"
            StreamCount = row.int "stream_count"
            RollingAvg7Day = row.float "rolling_avg_7day"
        })
    |> Seq.toList

/// Query to reconstruct state from events at a specific point in time
let getStateAtPointInTime<'T> (conn: SQLiteConnection) (streamId: string) (timestamp: DateTimeOffset) (deserialize: string -> 'T) =
    let sql = """
    SELECT data
    FROM events
    WHERE stream_id = @streamId
      AND timestamp <= @timestamp
    ORDER BY version ASC
    """
    
    let parameters = [
        "@streamId", SqlType.String streamId
        "@timestamp", SqlType.String (timestamp.ToString("o"))
    ]
    
    let events =
        Db.newCommand sql
        |> Db.setParams parameters
        |> Db.query conn (fun row -> row.string "data")
        |> Seq.map deserialize
        |> Seq.toList
    
    events

/// Find related streams based on similar event patterns
let findRelatedStreams (conn: SQLiteConnection) (streamId: string) (limit: int) =
    let sql = """
    WITH target_events AS (
        SELECT event_type, COUNT(*) as event_count
        FROM events
        WHERE stream_id = @streamId
        GROUP BY event_type
    ),
    stream_event_counts AS (
        SELECT 
            e.stream_id,
            e.event_type,
            COUNT(*) as event_count
        FROM events e
        WHERE e.stream_id != @streamId
        GROUP BY e.stream_id, e.event_type
    ),
    similarity_scores AS (
        SELECT
            sec.stream_id,
            SUM(
                CASE 
                    WHEN te.event_type IS NOT NULL THEN 
                        (sec.event_count * te.event_count) / 
                        (SQRT(sec.event_count * sec.event_count) * SQRT(te.event_count * te.event_count))
                    ELSE 0
                END
            ) AS similarity_score
        FROM stream_event_counts sec
        LEFT JOIN target_events te ON sec.event_type = te.event_type
        GROUP BY sec.stream_id
    )
    SELECT 
        s.stream_id,
        MAX(e.timestamp) as last_event_time,
        ss.similarity_score
    FROM similarity_scores ss
    JOIN events e ON ss.stream_id = e.stream_id
    JOIN streams s ON ss.stream_id = s.stream_id
    GROUP BY s.stream_id, ss.similarity_score
    ORDER BY ss.similarity_score DESC
    LIMIT @limit;
    """
    
    let parameters = [
        "@streamId", SqlType.String streamId
        "@limit", SqlType.Int limit
    ]
    
    type RelatedStream = {
        StreamId: string
        LastEventTime: DateTimeOffset
        SimilarityScore: float
    }
    
    Db.newCommand sql
    |> Db.setParams parameters
    |> Db.query conn (fun row ->
        {
            StreamId = row.string "stream_id"
            LastEventTime = DateTimeOffset.Parse(row.string "last_event_time")
            SimilarityScore = row.float "similarity_score"
        })
    |> Seq.toList
```

## Extension Methods for Donald

Add these extension methods to make working with SQLite JSON and Donald even easier:

```fsharp
module Donald.Extensions

open Donald
open System.Data.Common

type RowReader with
    /// Extract a JSON value from a column using SQLite's json_extract
    member this.jsonValue (column: string) (path: string) : string option =
        let columnValue = this.stringOrNone column
        match columnValue with
        | None -> None
        | Some json ->
            let cmd = this.Connection.CreateCommand()
            cmd.CommandText <- "SELECT json_extract(@json, @path)"
            
            let jsonParam = cmd.CreateParameter()
            jsonParam.ParameterName <- "@json"
            jsonParam.Value <- json
            cmd.Parameters.Add(jsonParam) |> ignore
            
            let pathParam = cmd.CreateParameter()
            pathParam.ParameterName <- "@path"
            pathParam.Value <- path
            cmd.Parameters.Add(pathParam) |> ignore
            
            let result = cmd.ExecuteScalar()
            if isNull result then None else Some (result.ToString())

    /// Test if a JSON column contains a value at the specified path
    member this.jsonContains (column: string) (path: string) (value: string) : bool =
        match this.jsonValue column path with
        | None -> false
        | Some extractedValue -> extractedValue = value
```

## Usage Examples

Here are some practical examples of using these queries:

```fsharp
// Get the distribution of users without data policy by ASN
let analyzeUserGroups (dbPath: string) =
    use conn = new SQLiteConnection(sprintf "Data Source=%s;Version=3;" dbPath)
    conn.Open()
    
    let asnDistribution = getStreamsWithoutDataPolicyByASN conn
    
    printfn "Users without data policy decisions by network:"
    for asn in asnDistribution do
        printfn "ASN: %s, ISP: %s, Type: %s, Count: %d" 
            asn.ASnumber asn.ISP asn.ASNtype asn.StreamCount
    
    // Find all streams from a specific network (e.g., a major ISP)
    if asnDistribution.Length > 0 then
        let targetASN = asnDistribution.[0].ASnumber
        let streamsInNetwork = findStreamsWithASNButNoPolicy conn targetASN
        
        printfn "\nStreams from ASN %s without data policy:" targetASN
        for stream in streamsInNetwork do
            let location = 
                match stream.Country, stream.City with
                | Some country, Some city -> sprintf "%s, %s" city country
                | Some country, None -> country
                | None, Some city -> city
                | None, None -> "Unknown location"
            
            printfn "Stream: %s, Location: %s, Last seen: %s" 
                stream.StreamId location (stream.LastSeen.ToString("g"))

// Analyze event patterns over time
let visualizeEventTrends (dbPath: string) =
    use conn = new SQLiteConnection(sprintf "Data Source=%s;Version=3;" dbPath)
    conn.Open()
    
    let startDate = DateTimeOffset.UtcNow.AddMonths(-3)
    let endDate = DateTimeOffset.UtcNow
    
    let loginTrends = 
        analyzeEventPatterns conn "UserLoggedIn" startDate endDate
    
    printfn "Login trends over the past 3 months:"
    for day in loginTrends do
        printfn "Date: %s, Logins: %d, Unique Users: %d, 7-day Avg: %.1f" 
            (day.EventDate.ToString("yyyy-MM-dd"))
            day.EventCount
            day.StreamCount
            day.RollingAvg7Day

// Find streams with similar behavior patterns
let findSimilarUsers (dbPath: string) (targetStreamId: string) =
    use conn = new SQLiteConnection(sprintf "Data Source=%s;Version=3;" dbPath)
    conn.Open()
    
    let similarStreams = findRelatedStreams conn targetStreamId 10
    
    printfn "Streams with similar behavior to %s:" targetStreamId
    for stream in similarStreams do
        printfn "Stream: %s, Similarity: %.2f, Last active: %s" 
            stream.StreamId
            stream.SimilarityScore
            (stream.LastEventTime.ToString("g"))
```

## Optimization Tips for SQLite JSON Queries

1. **Add JSON indexes** if you frequently query specific JSON paths:
   ```sql
   CREATE INDEX idx_event_data_case ON events(json_extract(data, '$.Case'));
   ```

2. **Consider denormalization** for frequently accessed JSON properties:
   ```sql
   ALTER TABLE events ADD COLUMN event_case TEXT;
   UPDATE events SET event_case = json_extract(data, '$.Case');
   CREATE INDEX idx_event_case ON events(event_case);
   ```

3. **Use JSON path expressions efficiently** by being as specific as possible.

4. **Batch queries** when performing analysis over large datasets.

5. **Consider using SQLite's JSON_EACH function** for flattening arrays when needed.
