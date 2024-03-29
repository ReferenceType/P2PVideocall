# P2PVideocall
Voice-Video Call Wpf Application based on P2PNetwork. Allows users to have Videocall, Chat and Filetransfer securely over local network or Internet.
<br/>Tested and established stable calls between 1.204,5 km distance clients
<br/>
<br/>Release available https://github.com/ReferenceType/P2PVideocall/releases/
## Features
- Secure Videocall among connected peers
- H264 Encoding
- Secure Persisitent Chat
- Secure File-Directory Transfer with hash file integrity verification(TCP and UDP)
- High Performance Secure Screen Share based on DirectX 11 API (up to 60 fps @1080p single core).
## Technical features
- Secure Tcp & Udp Hole punching
- Reliable Udp channels and Jumbo Udp Message support
- Congestion Avoidance and Control
- Dynamic Jitter Buffer for Audio and Video
- Toast notifications
- Statistics data and cool settings for nerds

Application has powerfull and optimised backend which is a hobby for me. However, Front end is incomplete,and its quite boring to develop. Slowly, i intend to complete it.
### Note: 
Application runs on background when closed with X button, to shut it down fully you have to shutdown from hidden icons on your taskbar (like skype)
## How It works
<img src="https://user-images.githubusercontent.com/109621184/204115163-3c8da2c3-9030-4325-9f4a-28935ed98977.png" width=50% height=50%>

Each application is a Peer client to a Relay Server. This server is used as a randezvous point for Udp Holepunching. 
If holepunch is sucessfull, all Tcp & Udp traffic is send directly among peers.Otherwise it will be relayed over the server.
Tcp holepunch succes rate is lower than udp. All reliable communication is through Reliable Udp channel by default.
Relay server is included on releases and can be found on repository: https://github.com/ReferenceType/RelayServer

The back-end network core libraries are located on : https://github.com/ReferenceType/StandardNetworkLibrary
## How to setup
Launch the Relay Server console application. Launch the Videocall Application and write your Relay Servers Ip and Port and hit comnnect.
If any other application connects to same relay server it will apper on your list on main window. You can select and call that peer or transfer files or chat.

If you intend to use this application over Internet, You need to enable port forwarding on your router for your relay server. 
If you dont wanna bother with your ISPs dynamic IPs, you can use a DDNS service like NoIP.
or you can deploy relay server on a cloud etc.

## Images
### Call interface
- Call can be initiated by selecting a peer from left menu anc clicking call button. Other end will receive notification for confirmation.

<img src="https://user-images.githubusercontent.com/109621184/215311518-d8d2dbd0-55de-4510-9040-3ab4feae183f.png" width=50% height=50%>
<img src="https://user-images.githubusercontent.com/109621184/215311576-72bdbc93-b85e-4781-8a75-1be976efe3d3.png" width=50% height=50%>

### Share Screen
- You can share a screen during a call.
<img src="https://user-images.githubusercontent.com/109621184/215313213-d79025bc-7b7c-4075-bc07-eee562d99dcf.png" width=50% height=50%>

### Settings
- Provided varoious settings and displays for visualising.
<!---img src="https://user-images.githubusercontent.com/109621184/215311656-7389395a-33ea-4413-b464-f486f5437594.png" width=50% height=50%-->
<img src="https://github.com/ReferenceType/P2PVideocall/assets/109621184/159763c3-a94b-4f46-a67e-6fafa4676cfd" width=50% height=50%>
<!---               -->


### Background mode mini-window
- When application is on background, a mini camera window will appear. It is resizeable and it automaticaally disappears when main app is active.
<img src="https://user-images.githubusercontent.com/109621184/215311768-c2d55e74-6c73-4adf-9305-84bd0d60f794.png" width=20% height=20%>

### File transfer
- You can Drag and drop a single file or full directory tree of folders, and application will send them through secure channel.
- xxHash hashing is also applied here where receiver verifies the file integrity.
- Both TCP and UDP is supported for transport.
<img src="https://github.com/ReferenceType/P2PVideocall/assets/109621184/1a62c453-b537-48f1-b478-8e6b2a28b4f1" width=75% height=75%>

### Toast Notification
- Various windows toast notifications are implemented to notify user. They are similar to skype
<img src="https://user-images.githubusercontent.com/109621184/215311924-bbc95e0f-989b-4c99-83c5-a2946444f67c.png" width=50% height=50%>




