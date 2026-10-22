# TavernEssentials

What a modded *A Township Tale* server and its players need to get back to normal, in one
DLL, with nothing hosted.

**Status: 0.14.0.** Nine features, each with its own switch:

- **Social**: cross-server friends. Every player gets a key, the game's own friend card works
  again, and on any server running TavernEssentials friends see each other's names in green,
  get each other's friend icon on the map and get through friends-only voice. The main menu's
  friends list shows your friends again. Game clients and servers.
- **Board**: the main-menu server boards fill with the community server list, eating a board
  orb joins that server, and eating the leave orb on your wrist wheel brings you back to the
  menu. No keyboard, no trips back to the launcher. A server that turns you away says why.
  Game clients only.
- **Backend**: quick answers to the calls the game still makes to Alta's closed web API, where
  waiting on them hurts. Leaving a server (lost connection, kick, restart, holding Ctrl+Q)
  lands in the main menu instead of a black screen; the inbox, the customizer and your first
  join stop waiting. The inbox shows a one-line welcome, once. The town's tutorial boards,
  which never tick on community servers, are hidden. Game clients only.
- **Guard**: the server keeps its own rules. JoinGuard: a player plays under the id their login
  token carries, or not at all (logs by default). LogGuard: players' join tokens and passwords
  stay out of the server's logs. Servers only.
- **SettingsBoard**: the game's own in-VR server settings board, back on the wheel for a
  server's moderators, owners and admins (their `users.json` roles), with the server's real
  settings. Only they can change a server's settings from a game; the game itself lets anyone.
  Servers and game clients.
- **Names**: the server names players from the logins here and TavernLib's accounts instead of
  asking Alta (console commands by name, the postbox, the time-trial boards). Servers only.
- **Postbox**: send your friends items by mail. The friend wheel lists your mutual friends on
  the server and your friends who are away (players the server has seen before), and a send is
  checked before the box takes anything. Server, plus a small part on game clients.
- **ModSettings**: the padlocked fifth lever on the main menu's Settings Panel opens a Mods
  board with your other mods' settings, to change in the headset. Game clients only.
- **Discord**: the game's own Discord status names the server you are on (TavernLib servers
  all call themselves "Local Server"), how many are on it and how long you have been there.
  Nothing to install. Game clients only.

Social and Board began as two mods, TavernSocial 0.3.0 and TavernBoard 1.0.0. TavernEssentials
does everything the two did, and more, and keeps their keys, friend records and tokens where
they were.

## Install

Download the zip from this repository's Releases page: it holds `TavernEssentials.dll`, this
README and the licence. The same `TavernEssentials.dll` goes on game clients and on dedicated
servers. It works out which it is running on.

- **Game client:** put `TavernEssentials.dll` in the game's `Mods\` folder.
- **Server:** put the same file in the server's `Mods\` folder and restart it. Board stays off
  on a server by itself.

Needs MelonLoader and TavernLib (1.5.2, from TavernLauncher 1.8.4, is what it was built and tested against), which is
what TheModdingTavern launcher installs.

**Coming from TavernSocial or TavernBoard:** delete `TavernSocial.dll` and `TavernBoard.dll`
from `Mods\`. If either is still there, both copies would patch the same game methods, so
TavernEssentials leaves that feature off and says so in the log:

    TavernSocial.dll is still in Mods: delete it; TavernEssentials' Social feature is off until you do.

Nothing else to move. Your key, your friend records and the launcher's per-server tokens stay
where they are, and a server keeps its key (and so its fingerprint). On the first start your
old settings are carried into the new config file (see Settings).

## Social: friends on any server

### Why a key

Since Alta's services closed, no player identity crosses servers. Each server keeps its own
user list, and the same person has a different number on every server. Social gives each
player a keypair instead. The public key is the player, the same everywhere, and nobody has
to run a service to vouch for it. A server checks that a player really holds their key each
time they join.

A client on a server without Social (TavernEssentials or the older TavernSocial) sends five
hellos over about 35 seconds, gets no answer, and stops. The server logs one "unsupported
message type" line per hello and carries on.

### Your key

Your key is made the first time the game starts with Social, and lives at

    %APPDATA%\TheModdingTavern\tavernsocial\identity.priv.xml

next to the launcher's own token cache. **That file is you.** Your friendships are tied to
it. If you lose it, the people who know you by it lose you.

- **Back it up:** copy that one file somewhere safe. Or set `social_export_key=1` and the mod
  keeps a copy at `Documents\TavernSocial\identity-backup.priv.xml`. If your Documents folder
  syncs to a cloud drive, the copy goes there too; decide whether you want that.
- **Move to a new PC:** copy your key into the new PC's
  `%APPDATA%\TheModdingTavern\tavernsocial\` as `import.priv.xml` and start the game. A valid
  key replaces the one there. The replaced key is kept as `identity.replaced-<time>.priv.xml`,
  and the import file is renamed `import.done-<time>.priv.xml`. An invalid import changes
  nothing.
- **A damaged key file is never replaced.** The mod stops and says so in the log. Restore the
  file, or move it out of the folder to start over with a new key.

What the key does not protect against: any other mod, launcher addon or program running as
you can read that file. There is no sandbox. Keep it the way you would keep a password file.

A server's key lives at `UserData\TavernSocial\server.priv.xml` in the server's game folder.

### Friend cards

Pass the game's friend card the usual way on a server running Social: one player holds it out
and lets go, the other takes it within two seconds. Since Alta's services closed, the card
just hung in the air. Now:

- If both of you run Social (TavernEssentials, or TavernSocial 0.2.0 or later), the server
  asks both your games to sign the friendship, signs it too, and hands each of you the record.
  The game plays its "new friend" effect.
- If either of you does not, the game plays its "denied" effect and nothing is kept.

Your records live in

    %APPDATA%\TheModdingTavern\tavernsocial\friends\

one small text file per friend, named by their key's fingerprint. Back the folder up with your
key. A record is only kept if it carries three good signatures: the server's, your friend's
and your own. Your game adds its signature only when it saw you pass a card with that player,
so neither a server nor another player can put a friendship on your disk that you did not
make. A friendship made again later keeps the older record beside the new one.

On a server, Social answers the two questions the card asks the game's old social service
(are these two friends; make them friends), and answers the friends list the server asks for
each joining player with none, at once (TavernEssentials recognises friends itself). Everything
else that service was asked goes where it went before. Set `social_friends=0` to leave all
three as they were.

### Friends on any server

When you and a friend are both on a server running Social (TavernEssentials, or TavernSocial
0.3.0 or later), each of your games shows the server the other's signature from your record.
Once both have, the server tells both games, and each marks the other's avatar as a friend:
their name shows green. It does not matter which server made the record.

It takes both of you. A server never counts one copy alone, and your game only takes the news
about someone you hold a record with, so neither a server nor another player can make anyone
your friend.

Recognised friends also get each other's **friend icon on the map**. The server decides map
icons (the game asks it, once a second, whether the player shown counts the viewer as a
friend), so this needs TavernEssentials 0.5.0 on the server. And your game counts them for
**friends-only voice**.

Both of those go by player id, and a modified game can make its player go by someone else's
id. So a friend counts by id only while exactly one player goes by that id and it is the one
the server identified: on the server, the connection that logged in with it; in your game,
the avatar the server named for them. The moment anyone else takes the id on, it stops
counting, for your friend too, until it is theirs alone again.

### Your friends in the game's own lists

The main menu's friends list shows every friend you hold a record with, by the name they had
when you made it. The in-game friend lists show the same. Nobody's online status is shown:
there is no service to ask.

**Unfriend** removes the friend, and so do the tablet's **Remove Friend** and **Decline** in the
main menu's requests list. Every copy of your record with them moves into
`friends\removed\`: nothing is deleted, and no list shows that folder. Your game drops them at
once. From then on it hands every server running TavernEssentials 0.13.0 or later that you join
a hash of a note you signed, saying you removed that record (the note itself, which names them,
stays with you; see Privacy). When your friend's game shows that server the same record (they
are there with you, or they come later), the server takes it back on both sides and tells their
game, which drops you too: your green name, the friend icon on the map, friends-only voice and
your place on the postbox's wheel, and their menu stops listing you. Their game checks your
signature with your key from its own copy of the record, so nobody else can end a friendship
for you. Only a friend card passed again makes you friends, and a new card makes a new record
that no earlier removal touches. A server older than 0.13.0 that witnessed your old friendship,
and has not restarted since, may send the old record back for a new card; your game refuses it,
so pass the card on a server running 0.13.0. A second Remove changes nothing.

What their game learns, and when: right away if you are on the same server; otherwise when
they next play on a server you have played on since, within 90 days of your last visit there,
while you are still among the last 400 players that server's address book sends (see Privacy),
and among your 20 newest removals. A friend on an older game (TavernEssentials before 0.13.0,
TavernSocial) beside you loses you as a friend here at once (their log says you left), but
their menu keeps you. A server before 0.13.0 keeps nothing: there your friend's game shows your
name green until you leave, and after that lists you on its postbox wheel as a friend away, as
before.

Friends archived by an older version (0.5.0 to 0.12.x moved them to `friends\archived\`) stay in
the requests list: **Accept** brings one back, **Decline** removes them as above.

Adding friends from the menu does nothing, as before: friendships are made with the friend
card. "Invite to Server" opens an empty window (there are no Alta server groups any more).

### The Social Tablet and Recent Players

Summon the tablet from your wrist menu as always. Its first page, **Recent Players**, fills at
once: the players on this server now, then the ones you met here before, newest first. The main
menu's Recent Players tab lists everyone you have met, on any server. The game records who you
meet (on the server, nearby, in combat); TavernEssentials keeps them in
`%APPDATA%\TheModdingTavern\tavernsocial\recent.txt`: names, the server's name and when, and
the fingerprint once Social has recognised them. No player ids.

Tap a player:

- **Remove Friend** removes them, the same as Unfriend in the menu.
- **Add Friend** sends nothing: the page says to pass them a friend card. On a friend an
  older version archived, it brings them back, like Accept.
- **Mute** is the game's own.
- **Report** with a reason goes to the server you are on, which fires its console event
  `SocialTabletPlayerReported` naming both players by their logins (server with
  `social_reports=1`). A player you met on an earlier visit can't be reported: the server
  only knows the players who were there.
- Ban and Unban stay hidden, as the game decides.

In the main menu's tab, Add as Friend and Report say where to do them instead.

Rows are kept per server by the community list's name for it: TavernLib servers all tell the
game they are "Local Server". To try a report alone, set `social_recent_test=1` in the config:
the page then lists a pretend player, Tester, on every server you join, and reporting them
reaches the server, which logs it against "unknown".

### Ending a friendship by hand

Deleting or moving the file in `friends\` (named by their fingerprint; the first 8 characters
are what the log shows) ends the friendship for your game from your next join on, but tells
nobody: their menu keeps listing you, and a server you have both played on can still list you
on their postbox wheel as a friend away. Unfriend is what reaches their game. No server can put
the record back on your disk; only passing a card again does.

### How the check works

On joining, the client says hello. The server answers with a random challenge that names
itself (by its own key), the player's session and the player's name. The client checks that
the name is its own, signs the challenge, and sends it back with its public key. The server
checks the signature, remembers which key is behind that connection, and tells everyone
identified which session holds which key fingerprint. Each challenge can be answered once,
within 60 seconds. A connection proves one key: a different key proved later on the same
connection is refused, and the first one stays.

A server names each player by the id they logged in with. A player whose game changes its
own player id after joining (only a modified game can) is no longer identified there.

This is TavernSocial's protocol with two kinds added for friends away (13 and 14, below) and
two for removals (15 and 16), which TavernSocial ignores, so TavernEssentials works with
TavernSocial 0.3.0 on either side. Friends away needs TavernEssentials 0.9.0 or later on
both; a removal reaching your friend needs 0.13.0 on the server and on their game.

### Privacy

Everyone identified on a server can see everyone else's key fingerprint there, and a
fingerprint is the same on every server. That is what lets friends recognise each other from
server to server. It also means a fingerprint can be followed from server to server, the way
a username already can.

Your game shows a server your record with a friend only while that friend is identified on
it too, or when the friend has been identified there before (see friends away, below). The
server then knows you two hold a record, and when and on which server it was made. Other
players learn nothing from it: only the two of you are told.

A server running 0.9.0 or later keeps an address book, `UserData\TavernSocial\directory.txt`:
the key fingerprint, public key, login id and name of every player identified there, and when
they were last seen. Only a proof on that server writes an entry, never a name alone. It keeps
at most three keys per account name (a PC and a headset fit) and 2000 in all; past either, the
ones seen longest ago go first. On joining, your game gets short tags for the players in it
who are not there now (a hash of the server's fingerprint and theirs, different on every
server), and shows the server your records only with friends whose tag it was sent. So a
server learns only friendships with players who have played on it. The tags hide nothing from
someone who already knows your fingerprint: anyone who has seen it on one server and joins
another can work out your tag there and see whether you are among the last 400 players that
server's book sends.

A server running 0.13.0 or later also keeps the removals players hand it,
`UserData\TavernSocial\withdrawals.txt`: for each, a hash of the signed note, the remover's key
fingerprint and account name, their signature, and when it was first and last handed over. It
names nobody else. The server learns who was removed only from a show of that same record (the
friend's, or one your game made on that visit before you removed them), which tells it the
friendship anyway; a server that already knows both fingerprints and the
record's time and witness could confirm a guess. It keeps at most 20 a key and 20 an account
name, 1000 in all (past that, the oldest of the account holding the most go first), each for
90 days after its last hand-over. Your game hands every such server your 20 newest removals,
one every 7 seconds.

### Message types

Game message types in use by mods, as far as we know. Tell us about yours:

| type | used by |
|---|---|
| 0-30 | the game (30 is voice; CircuitsVoiceChat uses it) |
| 31 | Velaris Link |
| 32, 33 | TavernLib |
| 34 | Wolf's social companion mod (by its author's account) |
| 35 | TavernEssentials' Social (and TavernSocial before it) |

Social's own kinds on type 35: 1-6 the identity exchange, 7 reserved, 8-10 friend records,
11-12 friends here, 13 the address book's tags, 14 a friend away, 15 a removal handed to the
server, 16 a removal passed on to the removed friend's game.

## Board: the main menu, working

- **Server boards**: the four main-menu boards show the community server list: names, player
  counts, region/version/lock status on the info board. The VR keyboard filter, levers and
  scroll wheel all work as designed.
- **Board joins**: eating a server's orb authenticates you against that server the same way
  the launcher does (your first visit creates your account there) and connects. A password
  server is refused with a clear message instead of an endless connecting screen; join those
  from the launcher for now. A whitelisted server admits you from the board if your name is
  on its list, and refuses you with the same clear message if not (apply from the launcher:
  press Join, then Apply).
- **Your own account on each server**: a board join plays as the account that server has for
  you, the same one a launcher join to it uses. Up to 0.6.0 a board join to any server other
  than the one you launched to played under the launch server's number for you, which on that
  server is someone else's account or nobody's: a fresh character, or another player's.
  If you built a character through board joins before 0.7.0, it is still in that server's
  save under the old number; the server's admin can tell you which.
- **My Servers**: the My Servers board shows the servers you have joined, like favourites;
  Discover (and Public, Open) show every server. A server is added when you join it, from the
  board or from the launcher. The first start fills it once from the launcher's token cache,
  so the servers you already play on are there on day one. A server you joined that is not on
  today's list is hidden until it is back. The main menu's **Servers** page lists the same
  servers; **Leave server** there takes one off. The list is a plain text file,
  `%APPDATA%\TheModdingTavern\joined_servers.txt`, one address per line, safe to edit.
- **Servers where friends work**: a server where your game found TavernEssentials' friends
  working (its identity check confirmed you, and the server said it recognises friends and
  witnesses friend cards) gets a `*` after its name on the boards, and the info board says "TE
  friends". It is only what your own game saw on its visits: nothing is sent anywhere, the check
  needs the server's own Social key (TavernEssentials, or TavernSocial before it), and a server
  you have not joined is not marked. Stars at the end of a server's own name are not shown, so
  a name cannot pass for a mark. A visit where nothing answers, or where the server says friends
  are off, takes the mark off. The list is `%APPDATA%\TheModdingTavern\friend_servers.txt`. `board_marks=0` turns
  the marks off.
- **Leave orb**: the wheel's native return-to-menu orb actually returns you to the menu (grab
  it, bring it to your face, let go). Without the mod it quits the whole game.
- **Join refusals you can read**: when a server turns your join away (a whitelist, a ban, a
  token it will not take, JoinGuard), the game shows "Successfully connected to server." and
  tries again every 5 seconds for two minutes, because it sends every refusal that way. With
  TavernEssentials the loading screen shows the server's own reason ("Server refused the join:
  ..."), and the second refusal in a row for the same reason takes you back to the main menu
  with it. Refusals that waiting can clear (your last visit still on the server, a full server,
  a wipe lock, a save that failed to load) keep the game's tries. This works for every join,
  from the board or the launcher, and changes nothing on the server. `board_join_reasons=0`
  gives the game's own line back.
- **Quest and tutorial servers**: a server running the quest overworld or the tutorial scene
  gets that scene. The launcher does this by reading the server's auth response and passing
  `/questScene` or `/tutorialScene` before the game starts; a board join happens inside an
  already-running game, so the mod reads the same fields from the same handshake and picks
  the scene itself. A server that says both runs the tutorial scene, as with the launcher.

To get to the menu, join any server from the launcher as usual, then eat the leave orb: you
land in the menu with the boards live.

Your identity for board joins is the launcher's own per-server token store
(`%APPDATA%\TheModdingTavern\tokens`), shared both ways: servers you joined from the launcher
already know you, and servers you first join from the board work from the launcher afterwards.
A new identity is saved before the server first sees it, so a first join cut short (a lost
reply, a timeout, the game closing) never costs you your name on that server. If it cannot be
saved, the join stops there and says why. An identity file that is in use by another program,
or holds another name's identity, stops the join too and is never touched; one that is damaged
(empty, cut short) is kept beside it as `.broken-<time>` and a new identity takes its place.

If the community list is unreachable, the boards behave exactly like the stock menu; the last
good list stands once one has loaded. Once it has, the boards stop asking Alta's dead web API
underneath.

## Backend: no more waiting on Alta

The modded game still runs Alta's online client, so a few things ask Alta's closed web API
and wait: three tries over 6-7 seconds, then an error. Backend answers them at once, the way
the game's own offline mode would, or the way the failure already ended:

| what you notice | the call | answered with |
|---|---|---|
| leaving a server (lost connection, kick, server restart, holding Ctrl+Q, console `stop-mode`) leaves a black screen until you quit | a login refresh before the menu loads | done |
| the character customizer opens ~7 s late | the store rotation | empty |
| the first join after starting the game waits ~7 s | your public IP address | 0.0.0.0 (what the game kept after the failure) |
| the main menu's inbox spins most of the time | Alta's news statuses, player reports | our welcome, once (below); no reports |
| (log only) | "is your email verified" | yes; the only effect was a popup |
| (log only) the analytics queue re-sends its whole history every flush | analytics events | dropped |
| (log only) every spawn, the wrist menu asks whether you may open the server settings page | a permission check | no (what the failure already gave); SettingsBoard decides the bubble |

The inbox's other two calls are answered by the other features: group invites by Board (with
`board_my_servers=1`), friend requests by Social (with `social_lists=1`). On a server, Social
answers the one call the server makes for each joining player, their friends list, with none,
at once (TavernEssentials recognises friends itself).

Where Alta's news was, the inbox gets a welcome of ours, written into the DLL (nothing is
fetched): one line of thanks, as neither the inbox nor the popup board scrolls. The game pops
it up and keeps it in the inbox for that game; then
`UserData\TavernEssentials\welcome-seen.txt` is written (with the version that showed it) and
the welcome does not come again. Delete that file to see it once more. `backend_welcome=0`
leaves the inbox empty.

The town's tutorial boards (Alta's achievement boards, at Spawn, the Town Hall, the Tavern and
the rest) never tick on a community server: the server waits on Alta's achievements API before
it tracks anything, so every task on them stays open for good. Backend hides them, and the
achievement popup with them, on your own screen only: nothing is saved or sent, and players
without TavernEssentials still see them. The tutorial island's boards stay.
`backend_hide_achievements=0` shows them.

`backend_enabled=0` leaves every one of them to fail slowly, as before, and the boards show.

## Guard: the server keeps its own rules

Two server-side checks, each in its own folder under `Guard\` with its own `mode` line.
JoinGuard checks what a player's game asks for: `log` first, `refuse` once the log shows only
what it should. LogGuard keeps join tokens and passwords out of the server's logs, on from
the start.

### JoinGuard: your id is your own

A joining game sends its login token and, beside it, the player id it wants. The game's own
join check refuses the join when they differ. TavernLib 1.5.x replaces that check with its own,
which writes the same refusal and then overwrites it with a success, so on a stock TavernLib
server a modified game can play as any id: another player's character, while that player is
offline. TavernLib's token check before it only looks at the token.

JoinGuard puts the refusal back. Nothing else in the join check changes.

- `joinguard_mode=log` (the default) lets such a join in and logs it:
  `JoinGuard: "Name" (token id 1000000004) joined as id 1000000002: another account's id. ...`
- `joinguard_mode=refuse` turns it away with "Your game asked to join as another player.
  Update TavernEssentials (or TavernBoard) on your PC, or join from the launcher." JoinGuard
  reads the token with the same parser as TavernLib's check before it, so both see the same
  claims. It also turns away a token that the check before it let in but that does not hold
  exactly one id and one name, or whose id is not plain digits (no real login token has two
  ids; one with two can get past a check that reads only the first), with "This server could
  not read your login token. Join from the launcher." `log` logs it instead. A token nobody
  can read is refused by TavernLib's own check already, and that refusal is left as it is.
- `joinguard_mode=off` does not check.

The first join after each server start is logged once, whatever happened to it
(`JoinGuard: checked the first join (id 1000000004: its token's own id).`), so a guard that
has stopped seeing joins shows as a missing line.

Log first: players still on TavernEssentials 0.6.0 or older, or on TavernBoard, send the wrong
id on every board join except to the server they launched to (fixed in 0.7.0, see Board), and
refuse would turn them away until they update. When the log stays quiet, switch to refuse:
set `joinguard_mode=refuse` in the server's `config.txt` and restart the server.

### LogGuard: tokens stay out of the logs

A player's join token works as their password on the server, and a stock TavernLib server
writes it to its logs. LogGuard hides it, and the secrets that travel with it, before any log
gets the line:

- TavernLib's `User trying to join with JWT ...`, on every join;
- the game's `... Credentials: ...`, on every refused join;
- the account token and the server password in TavernLib's `With payload {...}`, on every
  launcher login to a server started without `/launcherauth`.

That covers MelonLoader's logs (`Latest.log`, `Logs\`, its console, and on docker the
container's own log, which copies `Latest.log`), the game's `unity-log.csv` and
`unity-error-log.csv` under the server's `Logs\logs\`, and the log events the remote console
sends. The lines keep their words; only the secret goes:

    [TavernLib.Patches.PlayerJoinFilter.FilterInvalidTokens]: User trying to join with JWT [token hidden]
    Error: Token was for a different user: 1000000004 , PlayerId: 1000000002, Credentials: [token hidden]
      "token": "[hidden]",
      "password": "[hidden]"

Names, ids and addresses stay. The join itself is not touched: it reads its token again on
its own. The first token hidden after each start is logged once
(`LogGuard: hid the first token this run, in TavernLib's log.`).

It cannot clean what was written before it was installed: older `Latest.log` and `Logs\`
files, the `unity-log` archives and old docker logs still hold the tokens of everyone who
joined then.

`logguard_mode=off` logs them as before.

## SettingsBoard: the server settings board, for staff

The game still carries its in-VR board for a server's settings: the server settings bubble on
the wheel opens it, page after page of rows (damage, damage taken, XP rates, PvP, hunger,
respawn time and more). Alta showed it only to the moderators its website listed for a
server, and the server's half of it never shipped with the game. SettingsBoard brings it back
for a server's own staff.

- **Who**: a player whose `users.json` roles hold `moderator`, `owner` or `admin`. On a server
  whose TavernLib has its roles API, a role you add or take away counts within a second, and the
  player's bubble follows at their next wheel open. On an older TavernLib, they rejoin first:
  it sends players their roles only when they join.
- **Where**: on a server running TavernEssentials 0.14.0 or later with `settings_board=1`, for
  a player whose game runs it too. Every other wheel stays as it was.
- **What changes**: only the rows you changed, each kept to its own range (the one the
  console's `settings changesetting` uses). A value changed from the console while your board
  was open stays. Saving, the autosave clock, the empty-server shutdown, the physics frame
  rates, customization and the player limit never change from the board.
- **The log**: one line per change, `SettingsBoard: "you" changed DamageX: 1 -> 2.`

**The guard is on either way.** The game itself lets any player's game send the server a whole
new set of settings, saving included, and saves it. On a server running TavernEssentials only
staff get through, also with `settings_board=0`; anyone else is refused and logged.

The board's command rows (Fill Community Boxes and the like) do nothing yet: the server logs
which one was pressed. The board's console button opens your own game's console, not the
server's.

`settings_board=0` on a server: no board for anyone, the guard stays. On your game: the bubble
stays hidden.

## Names and the postbox: mail between friends

The game turns a player id into a name through Alta's web API, which is gone: ~7 s, then an
error. Names answers those lookups on the server from the players connected now (by the login
their connection holds, which their game cannot change) and from TavernLib's accounts; a
player in neither is "no such player" at once. Console commands that take a player name, the
postbox and the names on the time-trial boards use it. `names_local=0` goes back to asking Alta.

Alta left the postbox half built: its friend wheel was never filled, and a send took your
items and coins before asking Alta for both names, so they were lost. With Postbox, putting
the first coin in fills the wheel with your mutual friends on this server (Social's friends,
checked the way the map checks them), sent to your game only. Pulling the handle is checked
before the box takes anything: you must be the player logged in under your id, the friend
one of yours here, and both names known. Otherwise nothing is taken, the box plays its reject
effect and the server log says why. Every send is logged:

    Postbox: Alice opened a postbox: the wheel lists Bob.
    Postbox: Alice sends 2 item(s) to Bob.

Mail waits in your friend's own postbox if they leave before collecting it. The game allows
three sends per friend until they collect.

**Friends away** (0.9.0): the wheel also lists friends who are not on the server, if they have
been identified on it since the server has run 0.9.0 (its address book, above). Your game shows
the server your record with each of them, the server checks it against the key the friend
proved there, and tells your game the friend's id on that server. The gift goes to that id's
postbox and waits there until they next join. A send to a friend away is refused, with nothing
taken, when anyone on the server goes by that id, or when TavernLib's accounts do not tie that
name to exactly that id: no account for the name any more, the name under another id, or another
account holding the id (TavernLib can hand a deleted account's id to a new player). Your friend's game needs nothing new; yours needs 0.9.0 (the wheel only shows
ids your game counts as friends, so a small patch adds your friends away just while it reads
them).

    Postbox: Alice opened a postbox: the wheel lists Bob, Carol (away).
    Postbox: Alice sends 1 item(s) to Carol, who is away (it waits in their postbox).

Needs Social's friends on the server (`social_enabled`, `social_identity` and `social_friends`
all 1) and TavernEssentials on the sender's game, with the same three on there too (if one is 0,
the game has no friends away and Postbox stays off on it).

## ModSettings: a Mods tab in the main menu

The Settings Panel in the main menu has five tab levers; the fifth was padlocked and did
nothing. TavernEssentials unlocks it (a gear and "Mods" on its pad): pull it and a board slides
down, like the other four tabs, with the settings your other mods keep in MelonLoader's own
preferences, so you can change them without editing files.

- The **Mod** drum (top left) picks a mod; the **Page** drum (top right) turns its pages.
- On/off settings are levers; choices and numbers are drums. A number shows only when the mod
  gave it a range (at most 21 steps). Text settings never show: they hold paths and tokens.
  Keybinds (the Input System's `Key`, or `KeyCode`) never show either: a drum of a hundred keys
  is no way to pick one.
- A change is saved to the mod's preferences file at once, and a mod that listens for changes
  applies it at once; others pick it up when the game restarts. When the file cannot be written
  (one of the mod's entries holds a value its file cannot, say a `[Flags]` value of several
  names), the change still reaches the mod, and the log says once why it was not saved.
- Hovering a row shows what the setting does; a setting no mod listens to for changes says
  "(applies after restart)". Hovering the Mod drum shows the mod's name, version, author and
  what it does.
- `mods_hide=` leaves mods out (their names as the board shows them, comma-separated).
  `mods_board=0` keeps the padlock.

### Show your settings on the Mods board

Keep your settings in MelonLoader's own preferences and they show up; there is nothing to
reference.

```csharp
var cat = MelonPreferences.CreateCategory("MyMod", "My Mod");
var fps = cat.CreateEntry("show_fps", true, "Show FPS", "Shows a frame counter on your wrist.");
var hud = cat.CreateEntry("hud_scale", 1.0f, "HUD scale", "Size of the HUD.",
                          validator: new ValueRange<float>(0.5f, 2f));
fps.OnEntryValueChanged.Subscribe((was, now) => ApplyFps(now));   // applies the moment it changes
```

- A `bool` is a lever. An enum is a drum of its names, split into words. An `int`, `float` or
  `double` is a drum only with a `ValueRange` (at most 21 steps). Strings, keybinds (`Key`,
  `KeyCode`), `[Flags]` enums, unranged numbers and entries created hidden never show, nor does
  a hidden category.
- Subscribe to `OnEntryValueChanged` and the change reaches you as the lever moves. With no
  subscriber the row's hover says "(applies after restart)".
- Hovering the Mod drum shows your `MelonInfo` name, version and author, and what your mod does
  from `[assembly: AssemblyDescription("...")]`. The board finds your mod by the category's id or
  name (case, spaces and punctuation ignored), else by the assembly that subscribed to a change.
- The board writes your category's file on every change. Keep every value one the file can
  hold: a `[Flags]` value of several names cannot be written, and then nothing in the category
  saves (the change still reaches you; the log says why once).

## Discord: your status names the server

The game has its own link to Discord (Alta's application, the Discord Game SDK): with Discord
running, your profile shows "Playing A Township Tale". TavernEssentials rewrites what it sends:

    In the main menu                        in the menu
    Exploring the Forest                    in a world: the game's own words for what you do,
    Some Server (3 of 10)                   the server's name from the community list, the count,
    12:34 elapsed                           and how long since you joined

The count needs the list's player limit, so an unlisted server shows none. Discord's Join
button is left off: it asked Alta's closed web API for the server. `discord_players=0` drops the
count; `discord_status=0` gives you the game's own status back. Discord's own setting to share
your activity still decides whether anyone sees it.

The log names each new place your status goes out with (the menu, a server) and Discord's
answer to it. What you are doing and the count change it often; those go out unlogged and are
counted on the next place's line. Refusals from Discord are logged when they start or change,
and the first status it takes after them says how many were refused.

On a server the game's own link to Discord stays off. A PC that hosts a server and plays on
it runs two copies of the game, and the server's would say "In Menu" for as long as it runs,
over the status of the game you play. `discord_on_server=1` lets the server connect again.

## Settings

`UserData\TavernEssentials\config.txt` in the game folder, written on the first start with
every setting at its default, each under a line or two saying what it does. After that the
file is never rewritten: a setting it has no line for (one a newer version brought, or a line
you deleted) is added at the end on the next start, at its default and under its comment, in
the file's own encoding (a file Notepad saved as Unicode stays Unicode), and the log names it.
Your own lines, values and comments stay as and where they are.

On that first start, whatever `UserData\TavernSocial\config.txt` and
`Mods\TavernBoard_config.txt` said is carried over under the new names, and the log says so.
The old files are left as they were and not read again.

| key | default | what |
|---|---|---|
| `social_enabled` | 1 | Social's switch |
| `social_identity` | 1 | the identity check with servers running Social; everything else in Social needs it, so 0 = Social off, like `social_enabled=0` |
| `social_friends` | 1 | server: witness friend cards, recognise friends, map icons; client: co-sign yours and keep the records |
| `social_lists` | 1 | client: your records in the game's friends lists, Unfriend removes, the requests list restores what older versions archived; friends-only voice |
| `social_recent` | 1 | client: the tablet's Recent Players page and the menu's tab from `recent.txt`, reports to the server you are on |
| `social_recent_test` | 0 | client: 1 = Recent Players also lists a pretend player, Tester, to try a report alone |
| `social_reports` | 1 | server: a tablet report fires `SocialTabletPlayerReported`, named by logins; Ban/Unban messages dropped |
| `social_export_key` | 0 | client: keep a backup copy of your key in `Documents\TavernSocial\` |
| `social_verbose` | 0 | log every step of the identity check |
| `board_enabled` | 1 | Board's switch (game client only either way) |
| `board_url` | community list endpoint | where the server list comes from |
| `board_refresh_seconds` | 30 | list refresh interval (minimum 10) |
| `board_verbose` | 0 | log every refresh and skip decision |
| `board_join_auth` | 1 | authenticate board joins per server; 0 = vanilla joins |
| `board_leave_orb` | 1 | leave orb returns to the menu; 0 = it quits the game |
| `board_quest_scene` | 1 | load the quest overworld or the tutorial scene on servers that ask for one; needs `board_join_auth=1` |
| `board_my_servers` | 1 | My Servers and the Servers page = the servers you joined; 0 = My Servers shows every server |
| `board_join_reasons` | 1 | a server's refusal shown on the loading screen, and no two minutes of tries for one waiting will not clear; 0 = the game's own line and tries |
| `board_marks` | 1 | a `*` after the names of servers where your game found TE's friends working, and "TE friends" on the info board; 0 = no marks |
| `backend_enabled` | 1 | game client: answer the dead Alta calls at once; 0 = they fail slowly, as before |
| `backend_welcome` | 1 | game client: a one-line welcome in the main menu's inbox, once (`welcome-seen.txt`); 0 = the inbox stays empty |
| `backend_hide_achievements` | 1 | game client: hide the town's tutorial (achievement) boards and the achievement popup; your view only; 0 = they show |
| `joinguard_mode` | log | server: `refuse`, `log` or `off` for a join whose id is not its token's |
| `logguard_mode` | on | server: `on` hides join tokens and passwords in the logs; `off` logs them as before |
| `names_local` | 1 | server: name players from the logins here and TavernLib's accounts; 0 = ask Alta |
| `settings_board` | 1 | server: moderators, owners and admins get the server settings board with the real settings; 0 = no board (only they can change settings from a game either way); client: 0 = the bubble stays hidden |
| `postbox_enabled` | 1 | server: the friend wheel (friends here and away) and the send check; client: friends away on the wheel; 0 = the game's own postbox, which loses what it sends |
| `mods_board` | 1 | game client: the Settings Panel's fifth lever opens a Mods board; 0 = it stays padlocked |
| `mods_hide` | (empty) | game client: mods the Mods board leaves out, by the name it shows (or the MelonPreferences category id), comma-separated |
| `discord_status` | 1 | game client: the game's Discord status names the server, the time there, and says "In the main menu" in the menu; 0 = the game's own |
| `discord_players` | 1 | game client: with how many are on the server, "(3 of 10)", when the list gives its limit |
| `discord_on_server` | 0 | server: 0 = the server never connects to Discord, so the game you play on the same PC keeps your status; 1 = it connects as the game always did |

A config file written by an older version gains the newer lines on the first start of the
new one, under a line naming that version:

    # Added by TavernEssentials 0.14.0: settings this file did not have yet, each at its default.

A value that is not `0` or `1` (or `true`/`false`, `on`/`off`, `yes`/`no`) keeps the default
and says so in the log.

## What you will see

Everything is logged under `[TavernEssentials]` in `MelonLoader\Latest.log`, each line
starting with the feature it comes from. Fingerprints are shortened to 8 characters; keys,
signatures and tokens are never logged.

On a client, at start, then on joining a server running Social:

    Social: recent players: 12 kept (...\tavernsocial\recent.txt).
    Social: tablet: 8/8 seams patched: recent players, the tablet's pages and the main menu's tab answer at once.
    Social: TavernEssentials 0.14.0 (client) starting: 3/3 seams patched, loading your identity; the game's friends lists come from your records.
    Board: ready: 8/8 list seams patched, join-auth on, leave-fix on, join reasons on, my-servers on, marks on, list = ... every 30s.
    Backend: ready: 8/8 stand-ins for Alta's dead web API.
    Backend: welcome: not handed out here yet (no ...\UserData\TavernEssentials\welcome-seen.txt); the main menu's inbox shows it.
    Backend: achievements: the town's tutorial boards are hidden here, and the achievement popup with them (backend_hide_achievements=1); nothing is saved or sent.
    JoinGuard: server only, so it stays off on a game client.
    LogGuard: server only, so it stays off on a game client.
    Names: server only, so it stays off on a game client.
    Postbox: ready: the friend wheel also lists your friends who are away (on a server with TavernEssentials 0.9.0 or later).
    SettingsBoard: ready: the game's server settings bubble shows on the wheel for moderators, owners and admins, on a server that runs the board.
    Mods: ready: the Settings Panel's padlocked fifth lever opens a Mods board with your mods' settings.
    Discord: ready: the game's Discord status will name the server as the community list does, with how many are on it, and say "In the main menu" in the menu.
    Board: community list live: 11 servers.
    Discord: the game's link to Discord is up (application 403426544737058816).
    Discord: status: "" / "In the main menu" (the game had "" / "In Menu").
    Discord: Discord took it.
    SettingsBoard: the server settings bubble is hidden (no staff role here).
    Board: My Servers: 3 server(s).
    Backend: welcome: handed to the inbox; ...\UserData\TavernEssentials\welcome-seen.txt is written (0.14.0), so it shows this game only.
    Social: identity AbCdEfGh loaded in 30 ms (...); sign 13 ms, verify 0 ms, tampering caught.
    Social: the game's friends lists show your friends now (wrapped the game's SocialClient).
    Board: the main menu's Servers page lists My Servers now, with Leave server (wrapped the game's GroupsClient).
    Board: join: authenticated on "some.host".
    Board: join: playing as id 1000000007, the id this server has for you (the game had 1000000002).
    Board: My Servers: added "Some Server" (some.host:1757): you joined it.
    Social: message type 35 ready (type patch: ...; a full frame is ... bytes).
    Social: identity AbCdEfGh confirmed by server XyZ01234.
    Social: friend Bob (QrStUvWx) is here: recognised by server XyZ01234.
    Social: friend Bob (QrStUvWx): nameplate marked as a friend.
    Social: friend Bob (QrStUvWx): counted as a friend for friends-only voice.
    Discord: status: "In the Overworld" / "Some Server" (2 of 10), since 1790000000 (the game had "In the Overworld" / "Local Server", join secret "-1").
    Discord: Discord took it.

The very first start after installing 0.6.0 or later adds one line about the token cache:

    Board: My Servers: first start, so it was filled once from the launcher's token cache: 3 server(s) (...). ...

Unfriend and Accept in the menu, and Leave server:

    Social: friend Bob (QrStUvWx): 1 record file(s) moved to friends\removed\, nothing deleted. Only a friend card passed again makes you friends.
    Social: friend Bob (QrStUvWx) removed. Their game is told on the TE servers you both visit.
    Social: friend Carol (MnOpQrSt): restored.
    Board: My Servers: left "Some Server" (some.host:1757): it is off My Servers; joining it again puts it back.

On your friend's game, when your removal reaches it:

    Social: Alice (AbCdEfGh) removed you as a friend.

On a server:

    Social: address book: 12 player(s) proved here before (...\UserData\TavernSocial\directory.txt).
    Social: removals: 3 kept (...\UserData\TavernSocial\withdrawals.txt).
    Social: TavernEssentials 0.14.0 (server) starting: loading the server key.
    Social: tablet reports: ready: a report from a player's tablet fires SocialTabletPlayerReported, the reporter named by their login.
    Board: game client only, so it stays off on a server.
    Backend: game client only, so it stays off on a server.
    Mods: game client only, so it stays off on a server.
    Discord: server: the game's link to Discord stays off here, so the game you play on this PC keeps your Discord status (discord_on_server=0).
    LogGuard: ready: join tokens and passwords are hidden in the game's logs and TavernLib's.
    JoinGuard: ready (joinguard_mode=log): a join whose id is not its token's is logged, not refused. ...
    Names: ready: player names come from the players here and TavernLib's accounts, never Alta.
    Postbox: ready: the friend wheel lists your mutual friends here and your friends away, and a send is checked before anything is taken.
    SettingsBoard: ready: moderators, owners and admins (users.json roles) get the game's server settings board; only they can change this server's settings from a game.
    Social: server key XyZ01234 loaded in 70 ms; sign 20 ms, verify 0 ms, tampering caught.
    Social: listening on message type 35 (...).
    Social: SomePlayer (1000000003): identity AbCdEfGh confirmed.
    Social: tablet: SomePlayer (1000000003) reported Other (1000000004) for Griefing; SocialTabletPlayerReported fired.
    Social: Bob (1000000004) and SomePlayer (1000000003) are friends here: both showed their record.
    Social: SomePlayer (1000000003) and Bob (1000000004): each other's friend icon on the map.
    Social: SomePlayer (1000000003) removed Bob (1000000004) as a friend; Bob's game was told.
    SettingsBoard: sent the server settings to "SomeMod" (first board this run; 12 command row assets loaded here).
    SettingsBoard: "SomeMod" changed DamageX: 1 -> 2.
    SettingsBoard: refused a server settings change from "SomePlayer": not moderator, owner or admin in users.json.

The first start takes a second or so longer: making a key is slow on the game's runtime. It
happens off the main thread, so the game does not stall.

## Building

One folder per feature, one entry file each: `Social\Game\SocialFeature.cs`,
`Board\BoardFeature.cs`, `Backend\BackendFeature.cs`, `Guard\JoinGuard\JoinGuardFeature.cs`,
`Guard\LogGuard\LogGuardFeature.cs`, `Names\NamesFeature.cs`, `Postbox\PostboxFeature.cs`,
`SettingsBoard\SettingsBoardFeature.cs`, `ModSettings\ModSettingsFeature.cs`,
`Discord\DiscordFeature.cs`. `Main.cs` is the host. It takes the process for a server when it
was started headless (`-batchmode`, `-nographics`) or as a server at all (`/start_server`: the
launcher's "show game" option starts a server with a window): it reads the config, starts each
feature that is on inside its own try/catch, and ticks them. `Shared\` holds what more than
one feature uses; it and `Social\Core\` stay free of the game and MelonLoader.

`build.ps1` compiles against your game install (set `$game` at the top) and fails on any
warning, if the DLL ends up referencing TavernLib, or if the game's JSON library (Newtonsoft)
is used anywhere outside `Board\`. It writes `TavernEssentials.dll` beside itself; `-Install`
also copies it into `Mods\`.

## Licence

MIT. See `LICENSE`.
