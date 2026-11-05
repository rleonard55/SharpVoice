# Response pill preferences

Approved in chat: clicking outside the response pill dismisses it and stops speech. Speech is independently configurable in Smart Ask settings and through a persistent on/off toggle in the pill. Response length offers Brief, Conversational and Detailed; explicit requests take precedence. Brief favors a bare year when that answers the question.

Use the existing Win32 pill, settings and configuration persistence. Observe outside mouse clicks while the pill is visible because it opens without taking focus. Preserve interactions with its children and conversation menu. Reuse the existing close path to cancel pending replies and speech without erasing conversation memory.

## Approaches

The selected approach keeps a single saved speech preference shared by settings and the pill. A temporary per-answer toggle would be less predictable and was not selected. Focus-loss dismissal alone misses clicks in the already-focused underlying app; observe mouse clicks instead.

Implementation defaults: Brief response length; preserve existing read-aloud behavior until the user switches speech off. Apply response length to answers and follow-ups, without shortening insertion requests. Keep the current application architecture and dependencies.
