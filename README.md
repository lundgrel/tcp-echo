# tcp-echo
Small server/client that tries to keep a TCP connection open


Edit server.json / client.json to set settings.

When running the client connects to server. Sends a message each 8--12 seconds. The server responds with the same data. 
Each minute the program logs statistics on connections. If connection is lost, the client re-connects.
