namespace FlowValidator
open FsOpCore

module Agent = 
    ()
(*
# Validation
- Execute the flow as-is to ensure it not broken - no cua needed
- Note: Repeatedly requesting transfer pin may not be feasible so stop just before
*)

(*
Mining
Give high level task to CUA to traverse the website and 
Get to the transfer pin location - via visual inspection.
Record for each action the following
- action (click x y)
- if click - then record element props (selector path, xpath, elementId, aria-label, css classes)
- url (page) mark if data was entered or user interaction was require, e.g. MFA
- Run the sequence through LLM to extract the data for the determinist flow.
- Combine the flows extracted from individual tasks for the final flow
*)



