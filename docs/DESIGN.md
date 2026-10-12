# Shipment Desk design and fidelity inventory

Static ES modules extend the existing .NET API; no independent frontend framework is required. The accepted ImageGen concept is a white, navy and teal operational desk with a 62/38 table/inspector split, open four-metric strip, and a five-step tour. Typography uses the system sans-serif stack. UI remains semantic HTML; generated concept imagery is not shipped as an interactive screenshot.

Core copy: Shipment Desk; Operations; Guided tour; Sign out; Shipment operations; Synthetic shipments. Real API requests.; New shipment; Completed on time; Overdue arrivals; Mean dwell; Open holds; Shipments; Evidence timeline; Documents; Record event; From shipment to evidence; Start guided scenario.

Functional additions to the concept: authenticated login, editable explicit scenario clock, denominator/exclusion note, bounded-list note, source IDs and observation times in evidence dialogs, document upload dialog, status dialog, accessible error feedback. They are necessary to explain the data and exercise the requested API flow. Runtime tracking numbers and values are actual API results, not fixed mockup text. The baseline is chosen to reproduce the concept's 50% / 1 / 3 h / 1 summary.

Responsive behavior stacks the inspector below the table at 800px. The metric strip becomes two columns; tour steps stack. Forms use native dialogs with labels, focus handling and visible errors. Authentication tokens live only in module memory and are cleared on sign out.

Browser plugin is not available in this session. Rendered QA uses Playwright, including desktop and mobile screenshots, the real authenticated scenario, filtering, error/recovery, document download, and console checks. The design concept and final screenshot are inspected using view_image before handoff. No private customer data is used in the recordings.

## Final comparison

Concept native size: 1505 × 1045. Final Playwright desktop/mobile screenshots were inspected against it with view_image. Layout, palette, typography, container geometry, controls and responsive behavior were compared. Inspector density and upload chrome were repaired; the source and upload dialogs preserve access to actual data while keeping the primary surface close to the reference. Runtime IDs, empty document state, editable clock, exclusion note and preview footer are intentional functional differences. See VERIFICATION.md for the complete ledger and evidence.
