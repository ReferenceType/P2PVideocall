# P2PVideocall
Voice-Video Call Wpf Application based on P2PNetwork with protobuf messages

## Features
- Videocall among connected peers 
- Chat
- File-Folder Transfer 
## Technical features
- Udp Hole punching
- Toast notifications
- Dynamic Jitter Buffer
- Statistics data for nerds
## How It works
<img src="https://user-images.githubusercontent.com/109621184/204115025-e96ac864-4d2a-4797-a9a3-61f9f07c72aa.png" width=50% height=50%>

Each application is a Peer client to a Relay Server. This server is used for randezvous point for Udp Holepunching. 
If holepunch is sucessfull all Udp data is send directly among peers.
All TCP data goes through the relay server there is no hole punching for that yet. 
Relay server can be found on repository: https://github.com/ReferenceType/RelayServerTest

The back end network system is located on : https://github.com/ReferenceType/StandardNetworkLibrary

## Images
<img src="https://user-images.githubusercontent.com/109621184/204114575-f8e5179e-72c2-411c-86fa-950292c73bb0.png" width=50% height=50%>
<img src="https://user-images.githubusercontent.com/109621184/204114701-bd8e4533-98c8-4750-a7f0-921a60162ea1.png" width=50% height=50%>
<img src="https://user-images.githubusercontent.com/109621184/204114748-b7a652a1-b81f-4f04-a969-02be05b7dc14.png" width=50% height=50%>

