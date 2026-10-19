# prog7312-2026-prog7312-part-1-ChippsEthan

## SmartX Part2

### Requirements

- .Net SDK 10.0
- Visual Studio 2026

### The features the app currently has:

Everything from Part 1 still works. You can register a new sensor and attach a file to it, and the app simulates live telemetry for all the sensors using float, int and bool.
There are thresholds for alerts regarding safe levels for each category.

New in Part 2:

- A navigation menu with the three pillars. The Real-Time Command Stream page is now active and the Network Topology page is still disabled.
- Switching between pages keeps everything as you left it (search, selected device, data).
- A live dashboard with a timeline chart for the selected device.
- Search and filter for devices by name, category or alert state.
- Manual override commands for pumps and relays, with an undo button.
- Suggested Actions and Automated Insights that predict problems before you search for them.
- A queue monitor that shows which packets were processed first.

The data is stored in memory.

### How to run it

1. Clone the repo and open `SmartX/SmartX.slnx` in Visual Studio.
2. The NuGet packages restore automatically when the solution opens.
3. Make sure the startup profile is "New Profile". It starts SmartX.API and SmartX.WPF together.
4. Press F5. The API runs on http://localhost:5106 (Swagger is at http://localhost:5106/swagger) and the WPF window opens.

If the API is not running the sidebar shows "API offline" in red. It turns green once the API is up.

To run it from the command line instead:

```
cd SmartX
dotnet restore
dotnet build
dotnet run --project SmartX.API --launch-profile http
dotnet run --project SmartX.WPF
```

Run the last two commands in separate terminals.


### How to test the queues and dictionaries

Simulate a high priority alert:

1. Open Real-Time Command Stream and click Moisture Sensor B in the list.
2. Click "Simulate critical alert".
3. The reading is sent to the API like a real device. It goes into the priority queue, shows up as PRIORITY in the "Recently processed" list, and the sensor turns CRITICAL.
4. Suggested Actions will now show things like "Turn ON Pump A".


Test that the priority queue skips the normal queue:

1. Click "Queue priority demo".
2. 60 normal packets are queued, then one critical packet is queued last.
3. The critical packet is still processed first. You can see this in the "Recently processed" list or at `GET /api/queues/stats`.

Test the dictionaries:

- `GET /api/devices/24:6F:28:A1:00:02` finds a device by MAC straight from the Dictionary.
- `GET /api/devices/24:6F:28:A1:00:02/history?count=50` returns the readings from the SortedDictionary in timestamp order. The timeline chart uses this.
- Registering a sensor with a MAC that already exists gives a 409 error.

Test the HashSet:

- Click a sensor and then "Silence node". After about 15 seconds it goes OFFLINE and only one disconnect alert appears, no matter how long you wait.
- `POST /api/alerts/test-duplicates?mac=24:6F:28:A1:00:02` tries to raise the same alert 5 times. It should say 1 new and 4 duplicates blocked.

Test the stack (undo):

1. Select Pump A and click "Turn ON".
2. Click "Shut down ALL actuators".
3. Click "Undo last command". The shutdown is reverted first, and clicking undo again reverts the pump command.

Test the prediction engine:

1. Select a sensor and click "Start gradual decline".
2. Within about 15 seconds Automated Insights warns that it is heading towards a critical level, before it actually gets there.

### Data structures used

- Queue: normal telemetry in first in first out order (TelemetryQueues).
- PriorityQueue: critical alerts skip the normal queue (TelemetryQueues).
- Stack: command history for undo (CommandService).
- Dictionary: live devices looked up by MAC address (DeviceRegistry).
- SortedDictionary: sensor history sorted by timestamp (HistoryStore).
- HashSet: unique active alerts so duplicates are ignored (AlertTracker).
- Dictionary of transition counts with a PriorityQueue for ranking: the suggestion engine (PredictionEngine).

### Login (Firebase)

The app needs a login. Authentication is done with Firebase Authentication (email and password). The WPF app signs in with Firebase, gets a token, and sends it to the API with every request. The API checks the token with Google's public keys.

To set it up with your own Firebase project:

1. Go to https://console.firebase.google.com and create a project.
2. Build > Authentication > Get started > Sign-in method > turn on Email/Password.
3. Project settings > General and copy the Project ID and the Web API Key.
4. Paste the Project ID into `SmartX/SmartX.API/appsettings.json` (Firebase > ProjectId).
5. Paste the Web API Key into `SmartX/SmartX.WPF/Services/FirebaseConfig.cs`.

You can use the test account below or click "Create account" on the login screen:

- Email: (put your test email here)
- Password: (put your test password here)


### Links

Github: https://github.com/ChippsEthan/prog7312-2026-prog7312-part-1-ChippsEthan.git

Youtube Part 1: https://youtu.be/LjB1pL9jP1M

Youtube Part 2: (paste your Part 2 link here)


i declare i used artificial intelligence for help regarding the formatting and styling. 
