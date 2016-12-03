/*
Copyright (C) 2015,2016,2017 David Krauss, NX4Y

This program is free software; you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the 
Free Software Foundation; either version 2 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with this program;
if not, write to the Free Software Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA 02110-1301 USA

LiteDB.dll is free software licensed under the MIT license. See LITEDB on Git for information on litedb.
*/

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using LiteDB;

namespace pnxmono
{
    public partial class sounds
    { }

    public class myState  
    {
        public int stunGroup
        { get; set; }
        public myState()
        { }
        public myState(int p_stunGroup)
        {
            stunGroup = p_stunGroup;
        }
    }
    public class UdpState
    {
        public IPEndPoint endpoint
        { get; set; }
        public UdpClient udpclient
        { get; set; }
        public UdpState()
        { }
        public UdpState(IPEndPoint p_endpoint, UdpClient p_udpclient)
        {
            endpoint = p_endpoint;
            udpclient = p_udpclient;
        }
    }
    public class clients
    {
        public TcpClient clientID
        { get; set; }
        public int stunID
        { get; set; }
        public string callsign
        { get; set; }
        public int status
        { get; set; }
        public clients()
        { }
        public clients(TcpClient p_clientId, int p_stunId, string p_callsign, int p_status)
        {
            clientID = p_clientId;
            stunID = p_stunId;
            callsign = p_callsign;
            status = p_status;
        }
    }
    public class times
    {
        public long timeAcc
        { get; set; }
        public int stunID
        { get; set; }
        public times()
        { }
        public times(long p_timeAcc, int p_stunID)
        {
            timeAcc = p_timeAcc;
            stunID = p_stunID;
        }
    }

    
    [Serializable]
    public class ConfigData
    {
        public int Id { get; set; }
        public string defaultTG { get; set; }
        public int defaultTimeout { get; set; }
        public bool useVoicePrompts { get; set; }
        public bool useCT { get; set; }
        public bool useLocalCT { get; set; }
    }
   
    public static class soundDictionary
    {
        public static Dictionary<string, byte[][]> _dict = new Dictionary<string, byte[][]>
        {
            {"10100",sounds.speech_ww },
            {"10101",sounds.speech_wwTac1 },
            {"10102",sounds.speech_wwTac2 },
            {"10103",sounds.speech_wwTac3 },
            {"10200",sounds.speech_na },
            {"10201",sounds.speech_naTac1 },
            {"10202",sounds.speech_naTac2 },
            {"10203",sounds.speech_naTac3 },
            {"10300",sounds.speech_Europe },
            {"10301",sounds.speech_EuTac1 },
            {"10302",sounds.speech_EuTac2 },
            {"10303",sounds.speech_EuTac3 },
            {"10310",sounds.speech_france },
            {"10320",sounds.speech_germany },
            {"10400",sounds.speech_Pacific },
            {"10401",sounds.speech_PacTac1 },
            {"10402",sounds.speech_PacTac2 },
            {"10403",sounds.speech_PacTac3 },
            {"startup",sounds.speech_systemstart },
            {"default",sounds.default_speech }
        };
    }

    class MainClass
    {
        public static UdpClient udpClient;
        static IPEndPoint localEndPoint;
        //static IPEndPoint remoteEndPoint;
        static IPEndPoint mcastRemoteEndPoint;
        static IPAddress mCastGroup;
        public static IPAddress localAddr;
        public static TcpListener tcpListener;
        public static Thread TCPListenThread;
        public static long radioID = 0;
        public static int tgID = 0;
        public static myState thisState = new myState();
        public static int mJoined = 0;
        public static object locker = new object();
        public static Timer handshakeTimer;
        public static Timer endoftransmissionTimer;
        public static Timer udpStarterTimer;
        public static Timer revertTimer;
        public static Timer mcastJoinReminderTimer;
        //public static Timer watchdogTimer;
        public static Stopwatch stopwatch;
        public static long maxDelayTime;
        public static DateTime txStartTime;
        public static DateTime txEndTime;
        public static int keyDownFlag = 0;
        public static int announceflag = 0;
        public static int systemState = -1;
        public static string tgString = "";
        public static int UDPThreadFlag = 0;
        public static int TCPThreadFlag = 1;
        public static int sendingToMcastFlag = 0;
        public static string currentTGString = "";
        public static string currentMcastGroup = "";
        public static bool announceNeeded = false;
        public static bool okToTalk = true; // allows us to interrupt an announcement if someone starts talking over it from the other end.
        // trying to clean this up and keep them as ints.
        public static int defaultTalkGroup = 0;
        public static int lastTalkGroup = 0;
        public static int currentTalkGroup = 0;
      //  public static byte[] buffer;
        public static int systemEnv = 0;  // 0 = unix, 1 = windows
        public static bool messageReceived = false;
        public static UdpState uState = new UdpState();
        public static byte[] silence = { 0x04, 0x0c, 0xfd, 0x7b, 0xfb, 0x7d, 0xf2, 0x7b, 0x3d, 0x9e };
        public static List<clients> connectedClients = new List<clients>();
        public static List<times> myTimes = new List<times>();
        public static List<Timer> eotTimers = new List<Timer>();
        public static string defTalkgroup;
        public static int defTimeout;
        public static bool useVoicePrompts;
        public static bool useCT;
        public static bool useLocalCT;
        // states for systemState
        public const int state_sending = 0;
        public const int state_receiving = 1;


            

        [STAThread]
        public static void Main(string[] args)
        
        {
           

            // find out if windows or linux
            int p = (int)Environment.OSVersion.Platform;
            if ((p == 4) || (p == 6) || (p == 128))
            {
                systemEnv = 0;
            }
            else
            {
                systemEnv = 1;
            }

            // get current directory
            string localPath = Directory.GetCurrentDirectory();
            Console.WriteLine("Local Path = " + localPath);

            if (File.Exists("MyData.db"))
            {
                ConfigData status = getDBData();
                useVoicePrompts = status.useVoicePrompts;
                defTalkgroup = status.defaultTG;
                defTimeout = status.defaultTimeout;
                useCT = status.useCT;
                useLocalCT = status.useLocalCT;
            }
            else
            using (var db = new LiteDatabase ("MyData.db"))
            {
                var configs = db.GetCollection<ConfigData>("config");
                    var myData = new ConfigData
                    {
                        defaultTG = "10100",
                        defaultTimeout = 60,
                        useCT = true,
                        useLocalCT = true,
                        useVoicePrompts = true
                    };
               
                defTalkgroup = "10100";
                defTimeout = 60;
                useCT = true;
                useVoicePrompts = true;
                configs.Insert(myData);
           }

            tgString = defTalkgroup;
            defaultTalkGroup = Convert.ToInt32(defTalkgroup);
            tgID = defaultTalkGroup;


            // init default Timer

            MyHttpServer.WebServer.monoLocalWS();
            // set up initial UDP thread
            createUDPThread(defaultTalkGroup);
            udpStarterTimer = new Timer(udpStarterCallback, null, 10, 10); // call udpStarter ever 10ms
            // initialize TCP system
            //watchdogTimer = new Timer(wdCallback,null, 10000, Timeout.Infinite);
            handshakeTimer = new Timer(timerCallback, null, 0, 3500);
            endoftransmissionTimer = new Timer(EOTCallback, thisState, -1, -1);

            // tg default revert timer
            revertTimer = new Timer(TGtimerCallback, null, Timeout.Infinite, Timeout.Infinite);

            // mcast join timer

            mcastJoinReminderTimer = new Timer(reminderCallback, null, 1000 * 15, Timeout.Infinite);

            // start listening for local connection from V.24 Device
            tcpListener = new TcpListener(IPAddress.Any, 1994);
            TCPListenThread = new Thread(new ThreadStart(ListenForTCP));
            TCPListenThread.Start();
        }
        private static void ListenForTCP()
        {
            TCPThreadFlag = 1; // allow tcp thread to run
            Console.WriteLine("Listening...");
            tcpListener.Start();
            while (TCPThreadFlag == 1)
            {
                //blocks until connection is made from local router
                DateTime saveUtcNow = DateTime.UtcNow;
                TcpClient client = tcpListener.AcceptTcpClient();
                Console.WriteLine(saveUtcNow.ToString() + " UTC :Connection request from " + client.Client.RemoteEndPoint.ToString());
                //create a thread to handle communication with connected client
                Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));
                clientThread.Start(client);
                TCPThreadFlag = 0;
            }
        }
        public static void ReceiveUDPCallback(IAsyncResult ar)
        {
            UdpClient u = (UdpClient)((UdpState)(ar.AsyncState)).udpclient;
            IPEndPoint e = (IPEndPoint)((UdpState)(ar.AsyncState)).endpoint;
            Byte[] receiveBytes = u.EndReceive(ar, ref e);
            if (String.Compare(e.Address.ToString(), localAddr.ToString()) != 0)
            {
                systemState = state_receiving;
                sendToV24(receiveBytes);
                if (revertTimer != null)
                {
                    revertTimer.Change(Timeout.Infinite, Timeout.Infinite); // kill it;
                    revertTimer.Change(defTimeout*1000, Timeout.Infinite); // restart it
                }
            }
            UDPThreadFlag = 0; // will restart listen in 10ms or so.
           
        }
        private static void HandleClientComm(object client)
        {
            byte[][] thisVal;
            TcpClient tcpClient = (TcpClient)client;
            // tcpClient.ReceiveTimeout = 10000;
            connectedClients.Add(new clients(tcpClient, 0, "", 0));
            NetworkStream clientStream = tcpClient.GetStream();
            byte[] message = new byte[4096];
            int bytesRead;
            while (true)
            {
                bytesRead = 0;
                try
                {
                    //blocks until a client sends a message
                    bytesRead = clientStream.Read(message, 0, 4096);
                }
                catch 
                {
                    DateTime saveUtcNow = DateTime.UtcNow;
                    //a socket error has occured
                    var len = connectedClients.Count;
                    for (int i = 0; i < len; i++)
                    {
                        if (tcpClient == connectedClients[i].clientID)
                        {
                            Console.WriteLine(saveUtcNow.ToString() + " UTC :Local host disconnected. Beginning reset process." );
                            tcpClient = null;
                            connectedClients.Clear();
                            ListenForTCP();
                        }
                    }
                    break;
                }
                if (bytesRead == 0)
                {
                    //the client has disconnected from the server
                    //   Console.WriteLine("Disconnected");
                    DateTime saveUtcNow = DateTime.UtcNow;
                    var len = connectedClients.Count;
                    for (int i = 0; i < len; i++)
                    {
                        if (tcpClient == connectedClients[i].clientID)
                        {
                            Console.WriteLine(saveUtcNow.ToString() + " UTC :Disconnected from " + connectedClients[i].callsign);
                            try
                            {
                                connectedClients.RemoveAt(i); //remove socket from list
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Couldnt remove socket -" + e.InnerException.ToString());
                            }
                            break;
                        }
                    }
                    break;
                }
                //message has successfully been received via TCP from V.24 port.
                // this section handles the handshake with the Quantar and voice packets to be sent out to multicast.
                if (bytesRead < 9)
                {
                    Console.WriteLine("Short header. " + bytesRead.ToString() + " bytes");
                }
                else
                {
                    string myString = ByteArrayToString(message, bytesRead);
                    if (myString.Substring(0, 10) == "0831000000")
                    {
                        int messageType = message[8];
                        int messageStunID = message[6];
                        DateTime connectTime = DateTime.UtcNow;
                        switch (messageType)
                        {
                            case 0x3f: //handshake packet
                                {
                                    // fill in the stunID for this socket if necessary
                                    var len = connectedClients.Count;
                                    for (int i = 0; i < len; i++)
                                    {
                                        if (tcpClient == connectedClients[i].clientID && connectedClients[i].stunID == 0)
                                        {
                                            connectedClients[i].stunID = messageStunID;
                                            Console.WriteLine(connectTime.ToString() + " UTC :Connected to" + tcpClient.Client.RemoteEndPoint.ToString());
                                            connectedClients[i].status = 1;
                                            soundDictionary._dict.TryGetValue("startup", out thisVal);
                                            if (useVoicePrompts) saysomething(thisVal);
                                            }
                                    }
                                    byte[] reply = StringToByteArray("083100000002FFFD73");
                                    reply[6] = (byte)messageStunID;
                                    clientStream.Write(reply, 0, reply.Length);
                                    break;
                                }
                            case 0xbf: // handshake packet
                                {
                                    byte[] reply = StringToByteArray("08310000000AFF07BF0103C200000000FF");
                                    reply[6] = (byte)messageStunID;
                                    clientStream.Write(reply, 0, reply.Length);
                                    break;
                                }
                            case 0x03: // P25 packet
                                {
                                    if (message[9] == 161) // affilate request
                                    {
                                        //	int AffID = message[25] * 65536 + message[26] * 256 + message[27];
                                        //endoftransmissionTimer.Change(Timeout.Infinite, Timeout.Infinite); //disable timer
                                        //	Console.WriteLine("Affiliate from " + AffID.ToString() + " From id " + messageStunID.ToString());
                                    }
                                    else
                                    {
                                        // voice packet, process.
                                        int len = connectedClients.Count;
                                        string thisData = (ByteArrayToString(message, bytesRead));
                                        if (message[9] == 101 && message[25] == 2) // get tgid if this was a clean packet
                                        {
                                            tgID = message[11] * 256 + message[12];
                                            // handle mcast drop
                                            if (mJoined == 1 && String.Compare(tgID.ToString(), tgString) != 0)
                                            { // new talkgroup possibly being selected
                                                if (tgID > 10099 && tgID < 10599) // make sure this is a valid group between 10100 and 10600
                                                {
                                                    udpClient.DropMulticastGroup(mCastGroup); // do immediate drop on previous group
                                                    Console.WriteLine("dropped multicast group " + currentMcastGroup);
                                                    udpClient = null;
                                                    mJoined = 0;
                                                    // join new mcast group
                                                    createUDPThread(tgID);
                                                    announceNeeded = true;
                                                    // if not default group, start timer.
                                                    if (tgID != defaultTalkGroup)
                                                    {
                                                        revertTimer.Change(defTimeout*1000, Timeout.Infinite); // start timer
                                                    }
                                                    else
                                                    {
                                                        revertTimer.Change(Timeout.Infinite, Timeout.Infinite); // dont start timer
                                                    }
                                                }
                                            }
                                            if (tgID != defaultTalkGroup)
                                            {
                                                revertTimer.Change(defTimeout * 1000, Timeout.Infinite); // restart timer
                                            }
                                        }
                                        // continue processing message....
                                        if ((thisData.Substring(36, 2) != "00") || (tgID < 3)) //ignore 00 records, they cause key up with analog signals!
                                        {
                                            if (message[9] == 102 && message[25] == 2) //get radio id if this was a clean packet
                                            {
                                                radioID = message[10] * 65536 + message[11] * 256 + message[12];
                                            }
                                            if (tgID > 10099 && tgID < 19999) // only work with valid groups.
                                            {
                                                if (stopwatch != null)
                                                {
                                                    //Console.WriteLine(stopwatch.ElapsedMilliseconds);
                                                    if (message[9] > 97 && message[9] < 116)
                                                    {
                                                        myTimes.Add(new times(stopwatch.ElapsedMilliseconds, messageStunID));
                                                        if (maxDelayTime < stopwatch.ElapsedMilliseconds) { maxDelayTime = stopwatch.ElapsedMilliseconds; }
                                                    }
                                                    stopwatch.Restart();
                                                }
                                                // valid packet recevied, reset EOTTimer & revert timer
                                                endoftransmissionTimer.Change(Timeout.Infinite, Timeout.Infinite); //disable timer
                                                endoftransmissionTimer.Change(800, 800); // restart it;
                                                if (revertTimer != null)
                                                {
                                                    revertTimer.Change(Timeout.Infinite, Timeout.Infinite); // restart it;
                                                    revertTimer.Change(defTimeout * 1000, Timeout.Infinite);
                                                }
                                                if (((thisState.stunGroup != messageStunID) && (keyDownFlag == 1)) || (message[9] == 161)) //|| (thisState.stunGroup == 0)) // make sure this is still the same client that started the transmission.
                                                {
                                                    if ((thisState.stunGroup == 0) && (tgID > 2))
                                                    {
                                                        thisState.stunGroup = messageStunID;
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("device stun group received:" + messageStunID.ToString() + " Should be: " + thisState.stunGroup);
                                                        Console.WriteLine("Message:" + ByteArrayToString(message, bytesRead));
                                                    }
                                                }
                                                else
                                                {
                                                    for (var i = 0; i < len; i++)
                                                    {
                                                        try
                                                        {
                                                            if (connectedClients[i].clientID == tcpClient)  // was != tcpClient
                                                            {
                                                                // make sure we've had time to fill in the stun id before trying to pass audio
                                                                if (connectedClients[i].stunID != 0)
                                                                {
                                                                    if (message[9] == 96)
                                                                    {
                                                                        byte[] reply = StringToByteArray("08310000000cFFFD030002020c0b0000000000");
                                                                        reply[6] = (byte)connectedClients[i].stunID;
                                                                        NetworkStream SpecialSocketStream = connectedClients[i].clientID.GetStream();
                                                                        connectedClients[i].clientID.NoDelay = true;
                                                                        SpecialSocketStream.Write(reply, 0, 19); // not sure what this is..
                                                                        SpecialSocketStream.Write(reply, 0, 19); // or this. might be leftover
                                                                    }
                                                                    if ((message[9] == 96) || (message[9] == 98) || (message[9] == 107))  // keep the transmitter keyed 
                                                                    {
                                                                        message[10] = 2;
                                                                        message[11] = 2;
                                                                        message[12] = 12;
                                                                    }
                                                                    if (keyDownFlag == 0)
                                                                    {
                                                                        Console.WriteLine("keydown from V24, STUN Id " + messageStunID.ToString());
                                                                        thisState.stunGroup = messageStunID;
                                                                        txStartTime = DateTime.UtcNow;
                                                                        keyDownFlag = 1;
                                                                        stopwatch = Stopwatch.StartNew();
                                                                        endoftransmissionTimer.Change(800, 800);
                                                                    }
                                                                    message[6] = (byte)connectedClients[i].stunID;
                                                                    message[7] = (byte)0xFD;
                                                                    try
                                                                    {
                                                                        // send to current multicast group.
                                                                        if (sendingToMcastFlag == 0)
                                                                        {
                                                                            Console.WriteLine("Sending to " + mcastRemoteEndPoint.Address.ToString());
                                                                            sendingToMcastFlag = 1;
                                                                        }
                                                                        udpClient.Send(message, bytesRead, mcastRemoteEndPoint);
                                                                        systemState = state_sending;
                                                                        endoftransmissionTimer.Change(800, 800);
                                                                    }
                                                                    catch (Exception e)
                                                                    {
                                                                        // remove i from the list
                                                                        Console.WriteLine(e.Message);
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    Console.WriteLine("No stun ID!");
                                                                }
                                                            }
                                                            else // this is the transmitting client. probably never get here anymore.
                                                            {
                                                                if (thisState.stunGroup == 0)
                                                                {
                                                                    thisState.stunGroup = connectedClients[i].stunID;
                                                                }
                                                            }
                                                        }
                                                        catch (Exception e)
                                                        {
                                                            Console.WriteLine("Client gone:" + e.InnerException.ToString());
                                                        }
                                                    }
                                                } //end check for valid stun id
                                            } // end local check
                                        }// end else, not affilate message
                                    }
                                    break;
                                }
                        }
                    }
                }
            }
        }
        public static void TGtimerCallback (object state)
        {
            byte[][] thisVal;
            if (tgID != defaultTalkGroup)
            {
                udpClient.DropMulticastGroup(mCastGroup); // do immediate drop on previous group
                Console.WriteLine("Revert timeout: Leaving group " + mCastGroup.ToString());
                udpClient = null;
                revertTimer.Change(Timeout.Infinite, Timeout.Infinite);
                mJoined = 0;
                // join default mcast group
                createUDPThread(Int32.Parse(defTalkgroup));
                soundDictionary._dict.TryGetValue("default", out thisVal);
                if (useVoicePrompts) saysomething (thisVal);
                tgID = defaultTalkGroup;
            }
            }
        public static void udpStarterCallback(object state)
        {
            if (UDPThreadFlag == 0)
            {
                UDPThreadFlag = 1;
                udpClient.BeginReceive(new AsyncCallback(ReceiveUDPCallback), uState);
            }
        }
        public static void createUDPThread(int talkGroup)
        {
            // initialize UDP system
            // find the address of the adapter being using to get to the internet.
            UdpClient u = new UdpClient("8.8.8.8", 1);
            localAddr = ((IPEndPoint)u.Client.LocalEndPoint).Address;
            Console.WriteLine(localAddr.ToString());
            // convert talkgroup to a multicast address
            string mcastString = makeMulticastAddress(talkGroup);
            tgString = talkGroup.ToString();
            //bind on a network interface
            udpClient = new UdpClient();
            udpClient.ExclusiveAddressUse = false;
            udpClient.EnableBroadcast = true;
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 15);
            udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
            mCastGroup = IPAddress.Parse(mcastString);
            if (systemEnv == 0)
            {
                localEndPoint = new IPEndPoint(mCastGroup, 30000);
            }
            else
            {
                localEndPoint = new IPEndPoint(localAddr, 30000);
            }
            udpClient.Client.Bind(localEndPoint);
            udpClient.JoinMulticastGroup(mCastGroup);
            mcastRemoteEndPoint = new IPEndPoint(mCastGroup, 30000);
            Console.WriteLine("Joined multicast group at " + mCastGroup.ToString());
            uState.endpoint = localEndPoint;
            uState.udpclient = udpClient;
            udpClient.BeginReceive(new AsyncCallback(ReceiveUDPCallback), uState);
            UDPThreadFlag = 1; // allow udp thread to run
            mJoined = 1; // flag that we have joined a group.
        }
        public static void sendToV24(byte[] message)
        {
            if (connectedClients.Count != 0)
            {
                int i = 0;
                if (message.Length > 23)
                {
                    if ((message[14] == 0x04) && (message[23] == 0x9e))
                    {
                        // Console.WriteLine("probably silence");
                    }
                }
                {
                    // make sure we've had time to fill in the stun id before trying to pass audio
                    if (connectedClients[i].stunID != 0)
                    {
                        if (message[9] == 96)
                        {
                            byte[] reply = StringToByteArray("08310000000cFFFD030002020c0b0000000000");
                            reply[6] = (byte)connectedClients[i].stunID;
                            NetworkStream SpecialSocketStream = connectedClients[i].clientID.GetStream();
                            connectedClients[i].clientID.NoDelay = true;
                            SpecialSocketStream.Write(reply, 0, 19);
                            SpecialSocketStream.Write(reply, 0, 19);
                        }
                        if ((message[9] == 96) || (message[9] == 98) || (message[9] == 107))  // keep the transmitter keyed 
                        {
                            message[10] = 2;
                            message[11] = 2;
                            message[12] = 12;
                        }
                        if (keyDownFlag == 0)
                        {
                            Console.WriteLine("Keydown from multicast ");
                            okToTalk = false; // we cant talk if someone else is...
                            txStartTime = DateTime.UtcNow;
                            keyDownFlag = 1;
                            stopwatch = Stopwatch.StartNew();
                            endoftransmissionTimer.Change(800, 800);
                        }
                        message[6] = (byte)connectedClients[i].stunID;
                        message[7] = (byte)0xFD;
                        try
                        {
                            //Console.WriteLine("sending to v24 port");
                            // send to V24.
                            NetworkStream SocketStream = connectedClients[i].clientID.GetStream();
                            connectedClients[i].clientID.NoDelay = true;
                            SocketStream.Write(message, 0, message.Length);
                            endoftransmissionTimer.Change(Timeout.Infinite, Timeout.Infinite); //disable timer
                            if (announceflag == 0)
                            {
                                endoftransmissionTimer.Change(800, 800); // restart it;
                            }
                        }
                        catch (Exception e)
                        {
                            // remove i from the list
                            Console.WriteLine(e.Message);
                        }
                    }
                    else
                    {
                        Console.WriteLine("No stun ID!");
                    }
                }
            }
        }
        // make a valid multicast address out of a talkgroup ID (N4TCP Method)
        public static string makeMulticastAddress(int tg)
        {
            int x = 0;
            int b = 0; // 3rd octet
            int c = 0; // 4th octet
            x = tg - 10099;
            for (int i = 1; i < 1001; i++)
            {
                if (x < 254)
                {
                    c = x;
                }
                else
                {
                    x = x - 254;
                    b = b + 1;
                }
            }
            string region = tg.ToString().Substring(2, 1);
            string thisAddress = "239." + region+ "." + b.ToString() + "." + c.ToString();
            return thisAddress;
        }
        
        public static void reminderCallback(object state)
        {
   //         udpClient.JoinMulticastGroup(mCastGroup);
        }
        
        /* hit this timer when no more data has come in for x ms, assume that means remote is not transmitting any more */
public static void EOTCallback(object state)
        {
            lock (locker)
            {
                byte[][] thisVal;
                Console.WriteLine("End of remote transmission");
                endoftransmissionTimer.Change(-1, -1);

                //Console.WriteLine("System State:" + systemState.ToString());
                if (systemState == state_sending)
                {
                    if (announceNeeded == true)
                    {
                        Console.WriteLine("talk start");
                        try
                        {
                            soundDictionary._dict.TryGetValue(tgID.ToString(), out thisVal);
                            if (useVoicePrompts) saysomething(thisVal);
                            announceNeeded = false;
                        }
                        catch
                        {
                            Console.WriteLine("Missing voice file");
                        }
                    }
                }
                else
                {
                    if (useCT) saysomething(sounds.cTone2);
                }

                if (keyDownFlag == 1)
                {
                    Console.WriteLine("Local Unkey");
                    if (useLocalCT) saysomething(sounds.cTone);
                    txEndTime = DateTime.UtcNow;
                    keyDownFlag = 0;
                    stopwatch = null;
                    // statistics collection
                    var i = myTimes.Count();
                    long timeSum = 0;
                    string txDataSave = "";
                    for (i = 1; i < myTimes.Count; i++) // 1 based to ignore first entry
                    {
                        timeSum = timeSum + myTimes[i].timeAcc;
                        txDataSave = txDataSave + "/" + (myTimes[i].timeAcc).ToString();
                    }
                    long avgTime = (timeSum / i);
                    if ((int)avgTime != 0)
                    {
                        Console.WriteLine("Avg time was " + avgTime.ToString());
                        Console.WriteLine("Longest delay was " + maxDelayTime.ToString());
                    }
                    myTimes.Clear();
                    maxDelayTime = 0;
                    
                    sendingToMcastFlag = 0;
                }
            }
        }
       
        public static void timerCallback(object state)
        {
            lock (locker)
            {
                if (connectedClients != null)
                {
                    int len = connectedClients.Count;
                    for (var i = 0; i < len; i++)
                    {
                        // make sure we've had time to fill in the stun id before trying to pass audio
                        try
                        {
                            if (connectedClients[i].stunID != 0)
                            {
                                try
                                {
                                    try
                                    {
                                        byte[] message = StringToByteArray("083100000002AAFD01");
                                        message[6] = (byte)connectedClients[i].stunID;
                                        NetworkStream thisSocketStream = connectedClients[i].clientID.GetStream();
                                        thisSocketStream.Write(message, 0, message.Length);
                                    }
                                    catch (IOException e)
                                    {
                                        Console.WriteLine(e.Message);
                                    }
                                    catch (InvalidOperationException err)
                                    {
                                        if (connectedClients[i].clientID.Connected == false)
                                        {
                                            connectedClients.RemoveAt(i);
                                            Console.WriteLine("had to remove a client");
                                        }
                                        Console.WriteLine(err.Message);
                                    }
                                }
                                catch (Exception e)
                                {
                                    if (connectedClients[i].clientID.Connected == false)
                                    {
                                        connectedClients.RemoveAt(i);
                                        Console.WriteLine("Had to remove a client " + e.ToString());
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("Handshaking from " + connectedClients[i].clientID.Client.RemoteEndPoint.ToString());
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("error - " + e.InnerException.ToString());
                        }
                    }
                }
            }
        }

        /* utils */

        public static ConfigData getDBData()
        {
            ConfigData thisData = new ConfigData();
            using (var db = new LiteDatabase("MyData.db"))
            {
               
                var storedConfigData = db.GetCollection("config");
                var results = storedConfigData.FindAll();
                thisData.Id = results.First().RawValue["_id"].AsInt32;
                thisData.useVoicePrompts = results.First().RawValue["useVoicePrompts"].AsBoolean;
                thisData.defaultTG = results.First().RawValue["defaultTG"].AsString;
                thisData.defaultTimeout = results.First().RawValue["defaultTimeout"].AsInt32;
                thisData.useCT = results.First().RawValue["useCT"].AsBoolean;
                thisData.useLocalCT = results.First().RawValue["useLocalCT"].AsBoolean;
            }
            return thisData;
        }



public static void saysomething(byte[][] thingToSay)
        {
            okToTalk = true;
            systemState = state_sending;
            {
                keyDownFlag = 1;
                announceflag = 1;
                foreach (byte[] innerArray in thingToSay)
                {
                    if (okToTalk)
                    {
                        sendToV24(innerArray);
                        Thread.Sleep(10);
                    }
                    else break;
                }
                keyDownFlag = 0;
                announceflag = 0;
            }
            systemState = state_receiving;
        }

        public static string ByteArrayToString(byte[] ba, int bytesRead)
        {
            StringBuilder hex = new StringBuilder(bytesRead * 2);
            for (int i = 0; i < bytesRead; i++)
            {
                hex.AppendFormat("{0:x2}", ba[i]);
            }
            return hex.ToString();
        }
        public static byte[] StringToByteArray(String hex)
        {
            int NumberChars = hex.Length / 2;
            byte[] bytes = new byte[NumberChars];
            using (var sr = new StringReader(hex))
            {
                for (int i = 0; i < NumberChars; i++)
                {
                    bytes[i] = Convert.ToByte(new string(new char[2] { (char)sr.Read(), (char)sr.Read() }), 16);
                }
            }
            return bytes;
        }
      
    }
}
