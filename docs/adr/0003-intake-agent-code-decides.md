# 3. An intake agent drafts orders; code decides

Status: accepted

## Context
Engineers request tests in free text, in German and English. A coordinator retypes them into the
legacy application, one to three working days later, with transcription errors: neighbouring
formulation codes, the wrong axle, a customer's own procedure variant missed.

## Decision
- A model (Claude via the Messages API) drafts the order and quotes, for every field, the words it
  read it from.
- `IntakeValidator` accepts a draft only if each quote is in the request and says what the field
  says, the formulation is released, the application and procedure fit, and a customer with its
  own procedure variant gets that variant. Deadlines are computed by code from the quoted words.
- When the draft contradicts the request, the model gets the findings and one more read. When the
  request itself leaves something open (no axle, two formulation codes, an unreleased mix), the
  engineer is asked; nothing is guessed.
- Without an API key, a deterministic keyword reader stands in; the validator treats it the same.

## Consequences
- In the lab: no wrong orders (against 80 per 1,000 entered by hand), 14% of requests asked back
  (against 8% by the coordinator, who guesses more), about €11 of model cost per 1,000 requests.
