# P2PVideocall
Voice-Video Call Wpf Application based on P2PNetwork with protobuf messages.
Tested and achieved stable calls between 1.204,5 km distance clients

## Features
- Videocall among connected peers 
- Chat
- File-Folder Transfer 
## Technical features
- Udp Hole punching
- End to End encyrption on Tcp And Udp
- Toast notifications
- Dynamic Jitter Buffer
- Statistics data for nerds
## How It works
<img src="https://user-images.githubusercontent.com/109621184/204115163-3c8da2c3-9030-4325-9f4a-28935ed98977.png" width=50% height=50%>

Each application is a Peer client to a Relay Server. This server is used for randezvous point for Udp Holepunching. 
If holepunch is sucessfull all Udp data is send directly among peers.
All TCP data goes through the relay server there is no hole punching for that yet. 
Relay server can be found on repository: https://github.com/ReferenceType/RelayServerTest

The back-end network core libraries are located on : https://github.com/ReferenceType/StandardNetworkLibrary
## How to setup
Launch the Relay Server console application. Launch the Videocall Application and write your Relay Servers Ip and Port and hit comnnect.
If any other application connects to same relay server it will apper on your list on main window. You can select and call that peer or transfer files or simple write chat.
Chat is quite primitive and work in progress

## Images
<img src="https://user-images.githubusercontent.com/109621184/204114575-f8e5179e-72c2-411c-86fa-950292c73bb0.png" width=50% height=50%>
<img src="https://user-images.githubusercontent.com/109621184/204114701-bd8e4533-98c8-4750-a7f0-921a60162ea1.png" width=50% height=50%>
<img src="https://user-images.githubusercontent.com/109621184/204114748-b7a652a1-b81f-4f04-a969-02be05b7dc14.png" width=50% height=50%>

