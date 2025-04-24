# FlightDeck Architecture Documentation

This repository contains architectural documentation for the FlightDeck platform, detailing its evolution from a static site generator to a dynamic web application platform built on Falco.

## Documentation Overview

The documentation set consists of the following files, organized in a logical progression from vision to implementation details:

### [00 Falco-based Architecture.md](./00%20Falco-based%20Architecture.md)
Provides the high-level architectural vision for FlightDeck, outlining the transition to a Falco-based platform. This document serves as an executive summary and overview of the architectural approach, introducing key components like Falco, Oxpecker.Solid, and FsReveal.

### [01 Core Architecture.md](./01%20Core%20Architecture.md)
Details the core server architecture based on Falco, including the server structure, request processing flow, and implementation details for handlers, views, error handling, and performance optimizations. This document provides the foundation for understanding the server-side components.

### [02 Shared Domain Model.md](./02%20Shared%20Domain%20Model.md)
Explains the implementation of a shared domain model between client and server, ensuring type safety across the entire stack. This document covers domain types, API contracts, validation rules, and how to use shared types on both server and client.

### [03 Oxpecker.Solid Integration.md](./03%20Oxpecker.Solid%20Integration.md)
Describes the integration of Oxpecker.Solid for reactive UI components, including project structure, component implementation, state management, and advanced features. This document bridges the gap between server-side rendering and client-side interactivity.

### [04 FsReveal Integration.md](./04%20FsReveal%20Integration.md)
Details the integration of FsReveal for creating, managing, and delivering presentations directly from the FlightDeck platform. This document covers presentation domain models, FsReveal engine adaptation, and user workflows.

### [05 Build and Deployment Strategy.md](./05%20Build%20and%20Deployment%20Strategy.md)
Outlines the build pipeline, deployment options, continuous integration, and monitoring approaches for the FlightDeck platform. This document provides practical guidance for maintaining and deploying the application.

### [06 MVU-SolidJS Architecture.md](./06%20MVU-SolidJS%20Architecture.md)
Describes the implementation of the Model-View-Update (MVU) architectural pattern using SolidJS in a Falco-based web application. This document explains how to combine server-rendered pages with client-side interactivity while maintaining state across page transitions.

### [07 Custom Shape Masks.md](./07%20Custom%20Shape%20Masks.md)
Provides a comprehensive approach to implementing geometric shape masks for images in FsReveal presentations. This document details how to create circles, rhombuses, hexagons, and custom shapes using a type-safe F# DSL that generates SVG clip paths. It covers the domain model, rendering implementation, and practical usage patterns for creating visually engaging slides with masked images while maintaining F#'s type safety and functional programming benefits.

### [08 Complex Layouts in FsReveal.md](./08%20Complex%20Layouts%20in%20FsReveal.md)
Extends the presentation capabilities with advanced layout techniques, allowing precise positioning of content elements and complex background "landscaping" using SVG layers. This document details positioning systems, background layering, and how to combine these with shape-masked images to create professional slide designs. It includes practical examples and a fluent F# DSL for creating sophisticated visual compositions while maintaining code-first development approaches.

### [09 Event Sourcing with SQLite.md](./09%20Event%20Sourcing%20with%20SQLite.md)
Explores the implementation of event sourcing patterns using SQLite as the underlying storage mechanism. This document details how to design and implement an event-driven architecture that leverages SQLite's reliability and performance characteristics while maintaining the benefits of event sourcing such as complete audit trails, temporal queries, and system state reconstruction.

## Using This Documentation

These documents should be read in sequence for a comprehensive understanding of the FlightDeck architecture, though each document can also stand alone for reference on specific aspects. Key diagrams are provided in Mermaid format for easier visualization of architectural concepts.

Technical team members should refer to these documents when:
- Onboarding to the FlightDeck project
- Implementing new features
- Making architectural decisions
- Understanding the relationship between components
- Deploying or maintaining the application
- Creating visually rich presentations with custom layouts and shapes

## Implementation Status

The architecture described in these documents represents the target state for FlightDeck. Implementation is ongoing, with priorities determined by the development roadmap. Each component is designed to be implemented incrementally, allowing for progressive enhancement of the platform.
