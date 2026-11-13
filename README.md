# prog7312-2026-prog7312-part-1-ChippsEthan

## SmartX Part3

Youtube link:https://youtu.be/AOQ9ce_7Bt0   
Github Link: https://github.com/ChippsEthan/prog7312-2026-prog7312-part-1-ChippsEthan 
### Requirements

- .Net SDK 10.0
- Visual Studio 2026
- Windows (the client is WPF)
- Internet access (login uses Firebase Authentication)

No Docker is needed. The solution is a desktop client plus an API, and both run directly with the .NET SDK.

### The features the app currently has:

Everything from Parts 1 and 2 still works. You can register a new sensor and attach a file to it, and the app simulates live telemetry for all the sensors using float, int and bool.
There are thresholds for alerts regarding safe levels for each category.

New in Part 2:

- A navigation menu with the three pillars. The Real-Time Command Stream page is active.
- Switching between pages keeps everything as you left it (search, selected device, data).
- A live dashboard with a timeline chart for the selected device.
- Search and filter for devices by name, category or alert state.
- Manual override commands for pumps and relays, with an undo button.
- Suggested Actions and Automated Insights that predict problems before you search for them.
- A queue monitor that shows which packets were processed first.

New in Part 3:

- The third pillar, **Network Topology and Mesh Routing**, is now active in the menu and on the Home page, so all three pillars are linked.
- A live map of the mesh network (graph) with the gateway in the centre and the sensors around it.
- Route tracking for a sensor by MAC address or name, showing its path to the gateway with hops and power cost.
- A minimum spanning tree (Prim's algorithm) that shows the most power-efficient routes through the mesh.
- Self-healing: fail a node and the routes are recalculated, with a log of every sensor that was rerouted.
- Breadth-first and depth-first traversal to find every device still connected to the gateway and which are down or cut off.
- A historical telemetry log for a selected sensor, using an AVL tree for fast time-range queries (for example 14:00 to 16:00).
- Feedback fixes: login is verified by an API call, uploaded .csv/.txt files are read as live data, early warnings appear for readings close to their limits, and suggestions appear from the first alert.

The data is stored in memory.

### How to run it

1. Clone the repo and open `SmartX/SmartX.slnx` in Visual Studio.
2. The NuGet packages restore automatically when the solution opens.
3. Make sure the startup profile is "New Profile". It starts SmartX.API and SmartX.WPF together.
4. Press F5. The API runs on http://localhost:5106 (Swagger is at http://localhost:5106/swagger) and the WPF window opens.
5. Sign in. The app calls the API to verify your login before it opens, so if you see "Cannot reach the API", wait about 2 seconds and sign in again.

If the API is not running the sidebar shows "API offline" in red. It turns green once the API is up.

To run it from the command line instead:

```
cd SmartX
dotnet restore
dotnet build
dotnet run --project SmartX.API --launch-profile http
dotnet run --project SmartX.WPF
```

Run the last two commands in separate terminals, API first.


### How to test Part 3 (Network Topology and Mesh Routing)

Open "3. Network Topology and Mesh Routing" in the left menu. Wait about 10 seconds so all sensors have reported.

The map:

- The gateway is in the centre and the sensors are around it.
- Green thick lines are the minimum spanning tree (the power-efficient routes), grey lines are spare links, and red dashed lines go to a down node. The number on each line is its power cost.
- The header shows how many sensors reach the gateway and the total spanning tree cost (20 when everything is healthy).

Track a node by MAC address:

1. Type a MAC address (for example `24:6F:28:A1:00:02`) or part of a name into "Track a node" and press Track, or click a node on the map.
2. The amber path shows the route its data takes to the gateway, with the hops and power cost.

Test self-healing:

1. Click Moisture Sensor A and press "Fail node".
2. Within about 2 seconds the node turns red and the spanning tree is rebuilt without it (the total cost changes from 20 to 19).
3. The "Self-healing log" lists every sensor that was rerouted with its new path and cost. Press "Restore node" to bring it back.
4. You can also go to Real-Time Command Stream, select a sensor and click "Silence node". After about 15 seconds it goes OFFLINE and the map reroutes around it by itself.

Test BFS and DFS:

1. Press "Run BFS" or "Run DFS". They start from the selected node (or the gateway if none is selected).
2. Both list every reachable device in visit order with its hop count. Down or cut-off devices are listed as not reachable.

Test the historical log (AVL tree):

1. Click a sensor on the map.
2. Press "Last 5 min" or "Last hour", or type a time range such as 14:00 to 16:00 and press "Load range".
3. The line under the buttons shows how many nodes of the tree were visited compared with how many readings are stored. This proves the query did not scan the whole history.
4. `GET /api/devices/24:6F:28:A1:00:02/tree-stats` shows the tree size, height and rotations in Swagger.

Test the file import (uses the uploaded file as live data):

1. Create a file called `sample-temp.csv` with a header row and lines like `2026-11-12T08:00:00Z,24.5` (timestamps are UTC), or just one number per line.
2. In "1. Sensor Data Ingestion" register a new sensor and choose the file.
3. The status line says how many readings were imported. They go through the normal queues and alerts, appear in the live data, and appear in the Topology history grid for that sensor.

Test the early warnings and built-in suggestions:

1. Click "Pause device simulator" in the sidebar.
2. In Swagger call `POST /api/telemetry/float` (no token needed) with `{"deviceId":"24:6F:28:A1:00:01","value":23}`.
3. Automated Insights shows "Moisture Sensor A is close to its critical low limit" before it becomes critical. Send 55 to clear it and click "Resume device simulator".
4. Suggested Actions also include built-in safety rules (for example Turn ON the pump after low moisture), so suggestions appear from the first alert before any pattern has been learned.

API endpoints added in Part 3:

| Endpoint | Purpose |
|---|---|
| `GET /api/devices/{mac}/range?start=&end=` | AVL tree range query (UTC ISO timestamps) |
| `GET /api/devices/{mac}/tree-stats` | Tree size, height and rotations |
| `GET /api/mesh` | Graph snapshot: nodes, links, MST, BFS, reroutes |
| `GET /api/mesh/route/{mac}` | Path from a node to the gateway |
| `GET /api/mesh/traverse?start=&mode=bfs` or `dfs` | BFS or DFS traversal |
| `POST /api/mesh/nodes/{mac}/fail` and `/restore` | Failure simulation |
| `POST /api/sensors/{id}/upload` | Upload a file; .csv/.txt are imported as telemetry |


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

Part 3:

- AVL tree: timestamped telemetry per sensor, for fast time-range queries in O(log n + k) (AvlTree.cs, TelemetryTimeIndex.cs).
- Graph (adjacency lists): the mesh network, where vertices are the gateway and sensors and edges are wireless links weighted by power cost (MeshTopology.cs).
- Queue and Stack: breadth-first and depth-first traversal (MeshTopology.cs).
- Heap (PriorityQueue): Prim's minimum spanning tree for power-efficient routing (MeshTopology.cs).
- HashSet: visited nodes during traversal and spanning tree membership (MeshTopology.cs).

Parts 1 and 2:

- Queue: normal telemetry in first in first out order (TelemetryQueues).
- PriorityQueue: critical alerts skip the normal queue (TelemetryQueues).
- Stack: command history for undo (CommandService).
- Dictionary: live devices looked up by MAC address (DeviceRegistry).
- SortedDictionary: sensor history sorted by timestamp (HistoryStore).
- HashSet: unique active alerts so duplicates are ignored (AlertTracker).
- Dictionary of transition counts with a PriorityQueue for ranking: the suggestion engine (PredictionEngine).

The PDF report explains each structure in depth with code examples.

### Login (Firebase)

The app needs a login. Authentication is done with Firebase Authentication (email and password). The WPF app signs in with Firebase, gets a token, and then calls `GET /api/me` so the API validates the token before the app opens. The token is sent to the API with every request. The API checks the token with Google's public keys.

To set it up with your own Firebase project:

1. Go to https://console.firebase.google.com and create a project.
2. Build > Authentication > Get started > Sign-in method > turn on Email/Password.
3. Project settings > General and copy the Project ID and the Web API Key.
4. Paste the Project ID into `SmartX/SmartX.API/appsettings.json` (Firebase > ProjectId).
5. Paste the Web API Key into `SmartX/SmartX.WPF/Services/FirebaseConfig.cs`.

You can use the test account below or click "Create account" on the login screen:

- Email: ethanchipps409@gmail.com
- Password: Ethan@2004


### Files in this submission

- `README.md` this file
- `UPDATES.md` the changes made from the Part 1 and Part 2 feedback
- The PDF report: implementation report, project completion report and technology recommendations


### Links

Github: https://github.com/ChippsEthan/prog7312-2026-prog7312-part-1-ChippsEthan.git

Youtube Part 3: https://youtu.be/AOQ9ce_7Bt0  

## Declaration 

AI assistance was used to help investigate a Git version control issue that prevented code from being pushed correctly to the repository. The suggested steps helped the team identify the cause of the problem and resolve the Git workflow issue.
AI was also used to help with the formating and styling of the read me and other documents.


Youtube Part 1: https://youtu.be/LjB1pL9jP1M
