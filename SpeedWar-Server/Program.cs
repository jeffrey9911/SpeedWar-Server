using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Reflection.Metadata;

public struct Player
{
    // flag
    public bool isPlayerConnecting;

    // lv1 - player id
    public short playerID;
    public string playerName;
     // lv1 - networking
    public Socket playerTCPSocket;
    public IPEndPoint playerEndPoint;

    // lv2 - gameplay
    public string kartID;
    public short roomID;
    public short levelID;

    public float[] playerPosition;
    public float[] playerRotation;
    public float[] playerInput;



    // Level 1 Setup

    // TCP Setup constructor
    public Player(short consID, string consName, Socket consSocket)
    {
        isPlayerConnecting = true;

        playerID = consID;
        playerName = consName;

        playerTCPSocket = consSocket;

        levelID = 0;
    }

    // UDP Setup constructor
    private Player(short consID, string consName, Socket consSocket, IPEndPoint consEP)
    {
        isPlayerConnecting = true;

        playerID = consID;
        playerName = consName;

        playerTCPSocket = consSocket;
        playerEndPoint = consEP;

        levelID = 0;
    }
    // UDP setup method
    public Player SetupUDPEP(IPEndPoint consEP)
    {
        return new Player(playerID, playerName, playerTCPSocket, consEP);
    }



    // Level 2 Setup
    public Player(short consID, string consName, Socket consSocket, IPEndPoint consEP, string consKartID, short consLvID, short consRoomID)
    {
        isPlayerConnecting = true;

        playerID = consID;
        playerName = consName;

        playerTCPSocket = consSocket;
        playerEndPoint = consEP;

        kartID = consKartID;
        levelID = consLvID;
        roomID = consRoomID;

        playerPosition = new float[3];
        playerRotation = new float[3];
        playerInput = new float[2];
    }
    public Player CreateGame(string kartid, short lvid, short genRoomID)
    {
        return new Player(playerID, playerName, playerTCPSocket, playerEndPoint, kartid, lvid, genRoomID);
    }



    // Disconnect
    private Player(bool consFlag, short consID, string consName, Socket consSocket)
    {
        isPlayerConnecting = consFlag;

        playerID = consID;
        playerName = consName;

        playerTCPSocket = consSocket;
    }
    public Player ConnectOff()
    {
        return new Player(false, playerID, playerName, playerTCPSocket);
    }
}

public class ServerConsole
{
    // Resource Thread Control
    public static Mutex tcpMutex = new Mutex();
    public byte[] tcpReceiveBuffer = new byte[2048];

    public static List<Thread> threadList = new List<Thread>();
    public static Dictionary<short, Player> playerDList = new Dictionary<short, Player>();

    // flags
    static bool isPrintingPlayerList = true;
    static bool isUDPReceiving = false;
    static bool isTCPAccepting = false;

    // Connect
    private static Socket serverTCP;
    private static UdpClient serverUDP;

    

    static Random random = new Random();

    static void PrintPlayerList()
    {
        DateTime previousTime = DateTime.Now;

        double timer = 0.0;
        double interval = 1.0;

        while (isPrintingPlayerList)
        {
            DateTime currentTime = DateTime.Now;

            double deltaTime = (currentTime - previousTime).TotalSeconds;
            timer += deltaTime;

            if (timer >= interval)
            {
                if (playerDList.Count > 0)
                {
                    Console.WriteLine("===========[Server Player List]===========");
                    foreach (Player player in playerDList.Values)
                    {
                        Console.WriteLine("ID: {0}, Name: {1}, IsConnected: {2}", player.playerID, player.playerName, player.isPlayerConnecting);
                        Console.WriteLine("Level: {0}", player.levelID);
                        if (player.kartID != null) Console.WriteLine("Kart ID: {0}, Room ID: {1}", player.kartID, player.roomID);
                        //if (player.playerEndPoint != null) Console.WriteLine("UDP: {0}", player.playerEndPoint.Port);
                    }
                    Console.WriteLine("==========================================");
                }
                timer -= interval;
            }
            previousTime = currentTime;
        }
    }

    public static int Main(String[] args)
    {
        Console.WriteLine("[Take Input]: Please input server local IP:");
        string ipAddress = Console.ReadLine();

        Console.WriteLine("[Take Input]: Please input server local TCP Port: (UDP port will be set to TCP Port + 1)");
        int portNumber = int.Parse(Console.ReadLine());

        StartServer(ipAddress, portNumber);
        Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCancelKeyPress);

        Thread printPlistThread = new Thread(PrintPlayerList);
        threadList.Add(printPlistThread);
        threadList[threadList.Count - 1].Start();

        Console.WriteLine("[System Msg]: Server has started, press Ctrl+C or close the console window to quit.");
        Console.ReadLine();

        return 0;
    }

    static void StartServer(string ipA, int portN)
    {
        IPAddress serverIP = IPAddress.Parse(ipA);
        IPEndPoint serverTCPEP = new IPEndPoint(serverIP, portN);
        IPEndPoint serverUDPEP = new IPEndPoint(serverIP, portN + 1);

        serverTCP = new Socket(serverIP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        serverUDP = new UdpClient(serverUDPEP);

        try
        {
            serverTCP.Bind(serverTCPEP);
            serverTCP.Listen(50);

            isUDPReceiving = true;
            serverUDP.BeginReceive(ServerUDPReceiveCallBack, null); // UDP Receiving Thread

            isTCPAccepting = true;
            Thread tcpAcceptThread = new Thread(TCPAccept);         // TCP Accepting Thread
            threadList.Add(tcpAcceptThread);
            threadList[threadList.Count - 1].Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine("");
            throw;
        }
    }

    /// <summary>
    /// TCP Accept thread, recurrsive, keep accepting
    /// </summary>
    static void TCPAccept()
    {
        Console.WriteLine("[System TCP]: Accepting new players");
        try
        {
            Socket acceptedClientSocket = serverTCP.Accept();

            Thread playerTCPReceiveThread = new Thread(() => PlayerSetup(acceptedClientSocket));
            threadList.Add(playerTCPReceiveThread);
            threadList[threadList.Count - 1].Start();
        }
        catch (Exception ex)
        {

        }

        if (isTCPAccepting) TCPAccept();
    }

    /// <summary>
    /// Receives first TCP packet with header0. perfrom player login. Not recurrsive
    /// </summary>
    /// <param name="pSocket"> accepted socket from the player</param>
    static void PlayerSetup(Socket pSocket)
    {
        try
        {
            byte[] recvBuffer = new byte[1024];
            int recv = pSocket.Receive(recvBuffer);

            short header = GetHeader(recvBuffer, 0);

            if (header == 0)
            {
                string name = Encoding.ASCII.GetString(GetContent(recvBuffer.Take(recv).ToArray(), 2));

                short id = (short)random.Next(1000, 9999);
                while(playerDList.ContainsKey(id))
                {
                    id = (short)random.Next(1000, 9999);
                }


                playerDList.Add(id, new Player(id, name, pSocket));

                Console.WriteLine("[System TCP]: Player Created: {0}: {1}", id, name);

                if (playerDList.ContainsKey(id))
                {
                    string playerList = id.ToString() + name;

                    foreach (Player player in playerDList.Values)
                    {
                        if (player.playerID != id)
                        {
                            playerList += "#" + player.playerID.ToString() + player.playerName;
                        }
                    }

                    byte[] allPlayer = Encoding.ASCII.GetBytes(playerList);

                    pSocket.Send(AddHeader(allPlayer, 0)); // 0: Player registration

                    foreach (Player player in playerDList.Values)
                    {
                        if (player.playerID != id)
                        {

                            player.playerTCPSocket.Send(AddHeader(AddHeader(Encoding.ASCII.GetBytes(name), id), 1)); // 1: New player
                        }
                    }

                    PlayerTCPReceive(id);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            throw;
        }
    }

    /// <summary>
    /// TCP receive from specific player; Receive content handling
    /// </summary>
    /// <param name="pName"></param>
    static void PlayerTCPReceive(short pID)
    {
        try
        {
            int nullMessageCount = 0;
            while (playerDList[pID].isPlayerConnecting)
            {
                //Console.WriteLine("[System Warning]: Invalid message received: " + nullMessageCount);

                // shared resource should wait one here

                if (playerDList.ContainsKey(pID))
                {

                    byte[] recvBuffer = new byte[1024];
                    int recv = playerDList[pID].playerTCPSocket.Receive(recvBuffer);

                    short[] headerBuffer = new short[2];
                    Buffer.BlockCopy(recvBuffer, 0, headerBuffer, 0, 4);


                    switch (GetHeader(recvBuffer, 0))
                    {
                        case -1:
                            SwitchOffPlayer(pID);
                            break;
                        // Chat
                        case 2:
                            string content = Encoding.ASCII.GetString(recvBuffer, 4, recv - 4);

                            string chatPiece = $"[<{DateTime.Now.ToString("MM/dd hh:mm:ss tt")}> {playerDList[pID].playerName}]: {content}";

                            Console.WriteLine(chatPiece);

                            byte[] pieceMsg = AddHeader(Encoding.ASCII.GetBytes(chatPiece), 2);


                            foreach (Player player in playerDList.Values)
                            {
                                if(player.levelID == 0)
                                {
                                    player.playerTCPSocket.Send(pieceMsg); // 2: message to all clients
                                }
                            }

                            break;

                        case 3:
                            if(GetHeader(recvBuffer, 2) == pID)
                            {
                                if(playerDList.ContainsKey(pID))
                                {
                                    foreach(Player player in playerDList.Values)
                                    {
                                        if(player.playerID != pID)
                                        {
                                            player.playerTCPSocket.Send(recvBuffer); // 3: status update to all clients
                                        }
                                    }
                                }
                            }
                            break;

                        case 4:
                            if(GetHeader(recvBuffer, 2) == pID)
                            {
                                if(playerDList.ContainsKey(pID))
                                {
                                    short lvid = GetHeader(recvBuffer, 4);
                                    string kartid = Encoding.ASCII.GetString(GetContent(recvBuffer, 6));
                                    short[] rmid = {4, (short)random.Next(1000, 9999) };
                                    playerDList[pID] = playerDList[pID].CreateGame(kartid, lvid, rmid[1]);

                                    byte[] roomCreateMsg = new byte[4];
                                    Buffer.BlockCopy(rmid, 0, roomCreateMsg, 0, 4);


                                    playerDList[pID].playerTCPSocket.Send(roomCreateMsg);
                                }

                            }
                            break;



                        default:
                            nullMessageCount++;

                            if (nullMessageCount >= 100)
                            {
                                Console.WriteLine("[System Warning]: Invalid player. Perform player disconnection!");
                                SwitchOffPlayer(pID);
                            }
                            break;
                    }

                    
                }

                // release shared resource
            }


        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            SwitchOffPlayer(pID);
            throw;
        }
        finally
        {
            DisconnectPlayer(pID);
        }

    }

    /// <summary>
    /// Brake the connection and looping thread
    /// </summary>
    /// <param name="pID"></param>
    static void SwitchOffPlayer(short pID)
    {
        if (playerDList.ContainsKey(pID))
        {
            playerDList[pID] = playerDList[pID].ConnectOff();

            // Quit msg
            byte[] quitMsg = new byte[4];
            short[] shorts = { -1, pID };
            Buffer.BlockCopy(shorts, 0, quitMsg, 0, 4);
            foreach (Player player in playerDList.Values)
            {
                if(player.playerID != pID)
                {
                    player.playerTCPSocket.Send(quitMsg); // -1: to Client: disconnect
                }
            }

            Console.WriteLine("[System Warning]: Player{0} - {1} has been switch off from the server", playerDList[pID].playerID, playerDList[pID].playerName);
        }
        else
        {
            Console.WriteLine("[System Warning]: Player doesn't exist, nothing is switch off");
        }

    }

    /// <summary>
    /// Remove player
    /// </summary>
    /// <param name="pID"></param>
    static void DisconnectPlayer(short pID)
    {
        if (playerDList.ContainsKey(pID))
        {
            Console.WriteLine("[System Warning]: Player{0} - {1} has been removed from the server", playerDList[pID].playerID, playerDList[pID].playerName);
            playerDList[pID].playerTCPSocket.Close();
            playerDList.Remove(pID);
        }
        else
        {
            Console.WriteLine("[System Warning]: Player doesn't exist, nothing is removed");
        }
    }

    /// <summary>
    /// UDP receive thread, called from start server
    /// </summary>
    /// <param name="result"></param>
    static void ServerUDPReceiveCallBack(IAsyncResult result)
    {
        IPEndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);
        byte[] recvBuffer = serverUDP.EndReceive(result, ref clientEP);


        switch (GetHeader(recvBuffer, 0))
        {
            case 0:
                short pID = GetHeader(recvBuffer, 2);

                if (playerDList.ContainsKey(pID))
                {
                    playerDList[pID] = playerDList[pID].SetupUDPEP(clientEP);

                    Console.WriteLine("[System UDP]: Endpoint setup: " + playerDList[pID].playerEndPoint.Address + " " + playerDList[pID].playerEndPoint.Port);
                }
                break;

            case 1:
                short playerid = GetHeader(recvBuffer, 2);
                if (playerDList.ContainsKey(playerid))
                {
                    Buffer.BlockCopy(GetContent(recvBuffer, 4), 0, playerDList[playerid].playerPosition, 0, 12);

                    //Console.WriteLine("GET Position: {0}: {1}, {2}, {3}", playerid,
                    //    playerDList[playerid].playerPosition[0],
                    //   playerDList[playerid].playerPosition[1],
                    // playerDList[playerid].playerPosition[2]);

                    foreach (Player player in playerDList.Values)
                    {
                        if (player.playerID != playerid)
                        {
                            if (player.playerEndPoint != null)
                            {
                                serverUDP.Send(recvBuffer, recvBuffer.Length, player.playerEndPoint);
                            }
                            else
                            {
                                Console.WriteLine("[System UDP]: " + player.playerName + "'s endpoint is null");
                            }

                        }
                    }

                }
                break;

            default:
                break;
        }

        if (isUDPReceiving) serverUDP.BeginReceive(ServerUDPReceiveCallBack, null);
    }


    static short GetHeader(byte[] bytes, int offset)
    {
        short[] sheader = new short[1];
        Buffer.BlockCopy(bytes, offset, sheader, 0, 2);
        return sheader[0];
    }

    static byte[] AddHeader(byte[] bytes, short header)
    {
        byte[] buffer = new byte[bytes.Length + 2];
        short[] sBuffer = { header };
        Buffer.BlockCopy(sBuffer, 0, buffer, 0, 2);
        Buffer.BlockCopy(bytes, 0, buffer, 2, bytes.Length);
        return buffer;
    }

    static byte[] GetContent(byte[] buffer, int offset)
    {
        byte[] returnBy = new byte[buffer.Length - offset];
        Buffer.BlockCopy(buffer, offset, returnBy, 0, returnBy.Length);
        return returnBy;
    }


    /// <summary>
    /// On Concole task end: Socket close, Thread abort, Mutex dispose
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs args)
    {
        Console.WriteLine("[System Quit]: Server End Message");
        Thread.Sleep(1000);
        serverTCP.Close();
        serverUDP.Close();

        foreach (Thread thread in threadList)
        {
            thread.Abort();
        }
    }
}