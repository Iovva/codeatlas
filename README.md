# CodeAtlas

**Interactive Codebase Visualization Tool**

CodeAtlas is a local web application that analyzes public C#/.NET repositories and generates interactive dependency graphs to help developers understand architecture and assess change impact.

## Features

- 📊 **Interactive Graphs**: Namespace and file-level dependency visualization
- 🔍 **Dependency Analysis**: Static analysis using Roslyn compiler platform
- 🔄 **Cycle Detection**: Identify strongly connected components and circular dependencies
- 🎯 **Impact Analysis**: See what breaks when you change a component
- 🛤️ **Path Finding**: Visualize dependency paths between components
- 💾 **Export & Save**: PNG, SVG, and JSON export with save/load functionality

## Tech Stack

### Backend
- **.NET 8** - ASP.NET Core Minimal API
- **Microsoft.CodeAnalysis (Roslyn)** - Static code analysis
- **Git CLI** - Repository cloning and analysis

### Frontend (Planned)
- **Angular 19** - Modern web framework
- **Cytoscape.js** - Graph visualization
- **elkjs** - Graph layout algorithms
- **Bootstrap 5** - UI styling

## Quick Start

### Prerequisites
- .NET 8 SDK
- Git
- Node.js (for frontend development)

### Backend API

```bash
# Navigate to API directory
cd api

# Run the development server
dotnet run
```

The API will be available at `http://localhost:5000`

### Available Endpoints

- **GET /health** - Service health check
- **POST /analyze** - Analyze a repository (currently returns schema-correct stub)

#### Example Usage

```bash
# Test the analyze endpoint
curl -X POST "http://localhost:5000/analyze" \
  -H "Content-Type: application/json" \
  -d '{
    "repoUrl": "https://github.com/newtonsoft/newtonsoft.json",
    "branch": "master"
  }'
```

## Project Structure

```
/codeatlas
  /api          # ASP.NET Core Minimal API
    Program.cs              # Main entry point
    Models/                 # DTOs and data models
    appsettings.json       # Configuration
  /web          # Angular application (planned)
  README.md     # This file
  .gitignore    # Git ignore patterns
```

## Development Status

✅ **Step 1**: Project setup and development environment  
✅ **Step 2**: Backend skeleton with health endpoint  
✅ **Step 3**: /analyze endpoint contract and schema  
🔄 **Step 4**: Repository cloning and workspace management  

See [Implementation Plan](../memory-bank/IMPLEMENTATION_PLAN.md) for detailed roadmap.

## Documentation

- [Product Requirements](../memory-bank/PRD.md)
- [Software Design Document](../memory-bank/SDD.md)
- [Technical Stack](../memory-bank/TECHSTACK.md)
- [Launch Guide](../memory-bank/LAUNCH_GUIDE.md)
- [Progress Tracking](../memory-bank/progress.md)

## Contributing

This project follows a structured implementation plan. Please refer to the memory-bank documentation for architectural guidelines and development standards.

## License

This project is part of an academic dissertation on software visualization tools.



