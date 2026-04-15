# Match Maker Sample

This sample provides a match maker implementation that allows players to be grouped into public matches, start a
private match or join a private match using a room code.

## Scenes

The sample has two scenes: a 'MatchMaker' scene and a 'Game' scene, located in **Assets/MatchMakerSample/Scenes**. 

Players start in the 'MatchMaker' scene, which has a simple UI that allows players to enter a username, find a public
match, create a private match, or enter a room code and join a private match using the room code. Once players choose
one of those three options, they will connect to the match maker room and see a different UI showing the names of
players who were grouped into the same match as them, a message showing the state of the match, and a button allowing
them to leave the match, disconnect, and return to the first UI. Once the match begins, the match maker room sends the
players the information they need to connect to the game room, and the players disconnect from the match maker room,
load the 'Game' scene, and connect to the game room.

Because the match maker does not need to update or sync frequently, the 'Server Frame Rate' is set to 10 updates / second
and the 'Network Sync Rate' is set to 1 to sync every frame (10 syncs / second) in the *ksRoomType* inspector.

The 'Game' scene is very simple and has a UI that shows who was randomly chosen as the winner when the game is over, and
a button allowing players to return to the 'MatchMaker' scene and find another match.

Both scenes need to be added to Unity's scene list under **'File->Build Profiles->Scene List'** for the players to be
able to switch which scene is loaded.

## Scripts

All scripts are located in **Assets/MatchMakerSample/Scripts**.
 - Server scripts are located in **Assets/MatchMakerSample/Scripts/Server**.
 - Common scripts are located in **Assets/MatchMakerSample/Scripts/Common**

### Server Scripts

#### srMatchMaker

[srMatchMaker](https://github.com/KinematicSoup/examples/blob/main/MatchMakerSample/Assets/MatchMakerSample/Scripts/Server/srMatchMaker.cs)
is a server room script on the match maker room.

It assigns players to matches, starts rooms for matches and sends players to those rooms when they are ready.
Players can request to join a public match, start a private match and get a room code, or join a private match using a
room code using an RPC.

There is always one public match accepting players that any players who request a public match are put into. A
timer starts when the public match has the minimum number of players. A game room is launched for the match when
the timer reaches a set time or the match has the maximum number of players.

The player who creates a private match is the owner of the match, and the match will start when they send an
RPC to start the match if it has enough players. It will start automatically if it has the maximum number of
players. If the match owner disconnects, the first player who joined after the owner will become the new owner.

Players send their username as an authentation parameter when they connect to the matchmaker. A `Room.OnAuthenticate` handler is
registered that validates the number of authentication arguments the player sent and stores their username in a server player
script 'spPlayerMatchData'. When a player connects, it sets `player.IsVisible = false` to prevent data for that player from
syncing to other players, as we only want to sync player data to other players that are in the same match. Clients will not see
players that are not visible in the room's player list and will not get `Room.OnPlayerJoin` or `Room.OnPlayerLeave` events for those
players.

The matchmaker launches game rooms with an "authenticated" tag which tells the 'srGame' script that players need an authentation token
to authenticate. It sends an authentication token and the username for each player to the game rooms using a room-to-room
cluter RPC and waits for the game room to acknowledge it received the data using another cluster RPC. When the
matchmaker receives the acknowledgment, it sends each player their authentication token and the room info they will use
to connect to the game room.

If a room is launched for a match but the match is cancelled because players disconnected before the room is
ready to accept players, that room will become a stand-by room and will be used for the next match that starts.
There is never more than one stand-by room at a time, and if a second room's match is cancelled before it
starts, the room is shutdown instead. The match-maker sends periodic keep-alive cluster RPCs to the stand-by room to
prevent it from shutting itself down.

If an error occurs launching a game room, the match maker will try to launch it again up to a configurable number of
retries, after which it will cancel the match and send the players in that match an error code in an RPC.

#### Match

[Match] (https://github.com/KinematicSoup/examples/blob/main/MatchMakerSample/Assets/MatchMakerSample/Scripts/Server/Match.cs)
is a server script that tracks state for a match, including a list of players in the match. It has functions for
adding and removing players to/from the match. User names and player ids for the players in a match are sent to all other
players in the match using RPCs. The data cannot be sent as player properties because those would sync to all players and we
only want to sync this data to the players in the same match, and because the 'srMatchMaker' script set 
`player.IsVisible = false` which prevents player properties from syncing.

#### srGame

[srGame](https://github.com/KinematicSoup/examples/blob/main/MatchMakerSample/Assets/MatchMakerSample/Scripts/Server/srGame.cs)
is a server room script on the game room.

The game script receives authentication tokens for each player and their usernames from the srMatchMaker via a cluster RPC.
When players connect they authenticate using their unique authentication token. If the room was started without the
"authenticated" tag, it will allow players to pass authentication without an authentication token, which can be
useful for testing locally without having to first connect to a match maker.

A timer to start the game will start after the first player connects. If all players connect, the start game
timer is reduced to `StartTime` if it is larger.

The game ends after a set time or if there is only one player remaining, and a random winner is chosen. Players
are informed of who won with an RPC and the room shuts itself down using `Room.ShutDown()`.

The room shutsdown if no users connect after a set time, or if all players disconnect after
starting the game. The `srMatchMaker` script can send a keep-alive RPC to reset the shutdown timer when no players
are connected.

### Client Scripts

#### MatchMakerConnectHandler

[MatchMakerConnectHandler](https://github.com/KinematicSoup/examples/blob/main/MatchMakerSample/Assets/MatchMakerSample/Scripts/MatchMakerConnectHandler.cs)
is a *MonoBehaviour* on the match maker room for connecting to the match maker using a *ksConnect*
component when the user clicks a button to find a public match, or create or join a private match.

#### crMatchMaker

[crMatchMaker](https://github.com/KinematicSoup/examples/blob/main/MatchMakerSample/Assets/MatchMakerSample/Scripts/crMatchMaker.cs)
is a client room script on the match maker room. It handles RPCs from the match maker room for connecting to a game room and
updating the UI.

#### GameConnectHandler

[GameConnectHandler](https://github.com/KinematicSoup/examples/blob/main/MatchMakerSample/Assets/MatchMakerSample/Scripts/GameConnectHandler.cs)
is a *MonoBehaviour* on the game room for connecting to the game room using a *ksConnect* component. It has static
fields for a *ksRoomInfo* to connect to, an authentication token to connect with, and a scene name to load when the game is over and a
button is clicked. These fields are set by the 'crMatchMaker' before the scene is loaded. If they aren't set, the script will connect
using the connect process the *ksConnect* component is configured for.

#### crGame

[crGame](https://github.com/KinematicSoup/examples/blob/main/MatchMakerSample/Assets/MatchMakerSample/Scripts/crGame.cs)
is a client room script on the game room. It displays the winner and disconnects when it receives a game over RPC.

## Testing

You will need to use the [Unity Multplayer Play Mode](https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@1.6/manual/index.html),
[ParrelSync](https://github.com/VeriorPies/ParrelSync), or build a game client in order to connect multiple clients at once.

### Local Cluster

1. Build Reactor configs (**CTRL + F2**).
2. Open the 'MatchMaker' scene.
3. Open the **'Menu Bar->Reactor'** menu and select **'Launch Local Cluster'** to open the local cluster window.
4. Set the launch scene to 'MatchMaker' and the launch room to 'MatchMakerRoom'.
5. Set the 'Connect Mode' in the *ksConnect* inspector to 'Local'.
6. If you are using a build to connect the second client, do the build now.
7. Enter play mode.
8. Enter a username, then click the button to find a match or start a private match.
9. Connect a second client. If the first user started a private match, join it using the room code. Otherwise click the find a match button.
10. Both should be in the same match and you should see both usernames in the top left.
11. If you are in a public match, a count down should start to start the match. If the match is private, the first user has to click the star game button.
12. Both users should be moved to the 'Game' scene when the match starts. After several seconds, one player will be chosen as the winner.
13. Click the finish button to return to the 'MatchMaker' scene.

### Online Cluster

1. Open the **'Menu Bar->Reactor->Publishing'**.
2. Login using your KinematicSoup account.
3. Enter an image name and version in the form and click the **'Publish'** button.
4. Clusters can only be launched via the web console. Open https://console.kinematicsoup.com and login to your KinematicSoup account.
5. Select your project from the *'Projects'* or *'Shared Projects'* panel.
6. Select the *'Images'* tab.
7. Click **'Launch'** next to the image you just published.
8. Set the **'Scene'** to 'MatchMaker'.
9. Check the **'Launch Cluster'** checkbox.
10. Click the **'Launch'** button.
11. Set the 'Connect Mode' in the *ksConnect* inspector to 'Online'.
12. If you are using a build to connect the second client, do the build now.
13. Follow steps 7-13 from the Local Cluster testing section above.
14. When you are done testing, stop the cluster by clicking the **'X'** in the *'Stop'* column next to the cluster in the *'Sessions'* panel.