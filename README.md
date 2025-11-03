# RevitTools

A collection of MEP-focused productivity tools for Autodesk Revit, designed to streamline data extraction and analysis workflows.

## Overview

RevitTools is an add-in suite for Revit that provides utilities for mechanical, electrical, and plumbing (MEP) professionals. Currently in active development with a focus on modular architecture and multi-version support.

**Status:** Work in Progress | **Current Version:** 1.0.0-alpha

## Current Tools

### ValveGetter
Extracts valve service information from Revit models, including pipe system data, sizing, and location information.

**Status:** In Development  
**Features:**
- Automated valve detection and data extraction
- Service type identification

## Technical Architecture

### Project Structure
```
RevitTools/
├── lib                         # Common utilities and helpers
├── RevitAddin/                 # Main add-in entry point and ribbon UI
├── src/                        # Individual tool modules
│   └── ValveGetter/
│       ├── Core/               # Business logic
│       ├── Controller/         # Application flow
│       └── UI/                 # User interface
├── build/                      # Build configuration
└── solutions/                  # Version-specific solutions
```

### Key Features
- ~~**Multi-version support**~~: Built to support Revit 2023,~~2024, 2025+ with minimal code duplication~~
- **Modular design**: Each tool is self-contained and independently testable
- **Scalable architecture**: Easy to add new tools to the suite
- ~~**Shared build configuration**: Centralized version management via MSBuild props files~~

### Technologies
- C# / .NET Framework 4.8
- WPF for UI components
- Revit API
- ~~MSBuild for multi-version targeting~~

## Installation

**Note:** Installer not yet available. Currently requires manual build and deployment.

### Building from Source
1. Clone the repository
2. Open `solutions/RevitTools.[version].sln` in Visual Studio
3. Build configuration: `Release [Year]` (e.g., "Release 2024")
4. Post-build automatically deploys to: `C:\ProgramData\Autodesk\Revit\Addins\[Version]\`

### Requirements
- Visual Studio 2019 or later
- .NET Framework 4.8
- Revit 2023, 2024, or 2025
- Windows 10/11

## Usage

1. Open Revit
2. Navigate to the **Add-Ins** tab
3. Find the **Data Extractors** panel
4. Click **Valve Getter** to launch

## Development Roadmap

- [ ] Final touch up of ValveGetter
- [ ] Implement comprehensive error handling
- [ ] Create automated installer
- [ ] Add unit tests
- [ ] Create custom ribbon tab
- [ ] Additional tools 
- [ ] Multi-Version compatability

## Project Goals

This project demonstrates:
- **Clean architecture** with separation of concerns (Core/Controller/UI pattern)
- **Build system design** for managing multiple product versions
- **API integration** with complex third-party SDKs (Revit API)
- **Real-world problem solving** for AEC industry workflows
- **Maintainable code structure** designed for team collaboration and extension

## Contributing

Not currently accepting external contributions as this is a portfolio/learning project, but feedback and suggestions are welcome.



---

**Note to Reviewers:** This project is actively under development and represents ongoing work in software engineering, API integration, and domain-specific problem solving for the architecture/engineering/construction industry.
