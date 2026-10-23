namespace MazeRush.Monsters
{
    // CONTRACT. Implemented by the networked player (networking agent). Called by monsters on the server only.
    public interface IFreezable
    {
        void Freeze(float seconds);
    }
}
