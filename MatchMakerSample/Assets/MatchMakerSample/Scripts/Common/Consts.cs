namespace MatchMakerSample
{
    // RPC ids
    public class RPC
    {
        public const uint MOVE_TO_ROOM = 0;
        public const uint FIND_PUBLIC_MATCH = 1;
        public const uint START_PRIVATE_MATCH = 2;
        public const uint JOIN_PRIVATE_MATCH = 3;
        public const uint ADD_PLAYERS = 4;
        public const uint REMOVE_PLAYER = 5;
        public const uint LEAVE_MATCH = 6;
        public const uint CANCEL_MATCH = 7;
        public const uint AUTH_DATA = 8;
        public const uint KEEP_ALIVE = 9;
        public const uint NAME = 10;
        public const uint COUNTDOWN = 11;
        public const uint STARTING_GAME = 12;
        public const uint GAME_OVER = 13;
        public const uint ROOM_CODE = 14;
        public const uint START_GAME = 15;
        public const uint INVALID_ROOM_CODE = 16;
        public const uint OWNER = 17;
        public const uint FINDING_PLAYERS = 18;
    }

    // Property ids
    public class Prop
    {
        public const uint NAME = 0;
        public const uint MIN_PLAYERS = 1;
    }

    // Room tags
    public class Tags
    {
        public const string AUTHENTICATED = "authenticated";
    }

    // Reasons for cancelling a match.
    public class  CancelReason
    {
        public const uint REMOTE_DISCONNECT = 0;
        public const uint SERVER_ERROR = 1;
        public const uint TIMEOUT = 2;
    }

    public class Consts
    {
        // The number of characters in a room code.
        public const int ROOM_CODE_LENGTH = 4;
    }
}
