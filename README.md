# P2PVideocall
Voice-Video Call Wpf Application based on P2PNetwork with protobuf messages.
<br/>Tested and achieved stable calls between 1.204,5 km distance clients
<br/>
<br/>Release available https://github.com/ReferenceType/P2PVideocall/releases/tag/v0.2.0-alpha
## Features
- Videocall among connected peers 
- Persisitent Chat, where chat history loaded on demand as you scroll up
- Secure File-Folder tree Transfer
- Screen Share
## Technical features
- Secure Udp Hole punching
- End to End encyrption on Tcp And Udp
- Toast notifications
- Dynamic Jitter Buffer for Audio and Video
- Statistics data for nerds
## How It works
<img src="https://user-images.githubusercontent.com/109621184/204115163-3c8da2c3-9030-4325-9f4a-28935ed98977.png" width=50% height=50%>

Each application is a Peer client to a Relay Server. This server is used for randezvous point for Udp Holepunching. 
If holepunch is sucessfull all Udp data is send directly among peers.
All TCP data goes through the relay server there is no hole punching for that yet. 
Relay server can be found on repository: https://github.com/ReferenceType/RelayServer

The back-end network core libraries are located on : https://github.com/ReferenceType/StandardNetworkLibrary
## How to setup
Launch the Relay Server console application. Launch the Videocall Application and write your Relay Servers Ip and Port and hit comnnect.
If any other application connects to same relay server it will apper on your list on main window. You can select and call that peer or transfer files or simple write chat.
Chat is quite primitive and work in progress

## Images
### Call interface
- Call can be initiated by selecting a peer from left menu anc clicking call button. Other end will receive notification for confirmation.

<img src="https://user-images.githubusercontent.com/109621184/215311518-d8d2dbd0-55de-4510-9040-3ab4feae183f.png" width=50% height=50%>
<img src="https://user-images.githubusercontent.com/109621184/215311576-72bdbc93-b85e-4781-8a75-1be976efe3d3.png" width=50% height=50%>

### Share Screen
- you can share a screen during a call. Image looks like inceptions because im sharing to myself.
<img src="https://user-images.githubusercontent.com/109621184/215313213-d79025bc-7b7c-4075-bc07-eee562d99dcf.png" width=50% height=50%>

### Settings
- Proviced varoious settings and displays for visualising whats going on. 
<img src="https://user-images.githubusercontent.com/109621184/215311656-7389395a-33ea-4413-b464-f486f5437594.png" width=50% height=50%>

### Background mode mini-window
- When application is on background a mini camera window will appear. It is resizeable and it automaticaally disappears when main app is active.
<img src="https://user-images.githubusercontent.com/109621184/215311768-c2d55e74-6c73-4adf-9305-84bd0d60f794.png" width=20% height=20%>

### File transfer
- You can Drag and drop a single file or entire directory tree of folders, and application will send them through SSL.
- Md5 hashing is also applied here where sender and receiver verifies the file integrity.
<img src="https://user-images.githubusercontent.com/109621184/215311839-68291f99-78e6-43ca-aaf3-f496229c28b6.png" width=50% height=50%>
<img src="https://user-images.githubusercontent.com/109621184/215311845-3be48d95-4363-48dc-bcff-3f0431bccb42.png" width=50% height=50%>

### Toast Notification
- Various windows toast notifications are implemented to notify user. They are similar to skype
<img src="https://user-images.githubusercontent.com/109621184/215311924-bbc95e0f-989b-4c99-83c5-a2946444f67c.png" width=50% height=50%>




