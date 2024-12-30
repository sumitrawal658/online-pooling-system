namespace PollSystem.Mobile.Services
{
    public class GroupManager
    {
        private readonly Dictionary<string, HashSet<string>> _groupSubscriptions;
        private readonly object _lock = new object();

        public GroupManager()
        {
            _groupSubscriptions = new Dictionary<string, HashSet<string>>();
        }

        public void AddSubscription(string groupId, string connectionId)
        {
            lock (_lock)
            {
                if (!_groupSubscriptions.TryGetValue(groupId, out var connections))
                {
                    connections = new HashSet<string>();
                    _groupSubscriptions[groupId] = connections;
                }
                connections.Add(connectionId);
            }
        }

        public void RemoveSubscription(string groupId, string connectionId)
        {
            lock (_lock)
            {
                if (_groupSubscriptions.TryGetValue(groupId, out var connections))
                {
                    connections.Remove(connectionId);
                    if (connections.Count == 0)
                    {
                        _groupSubscriptions.Remove(groupId);
                    }
                }
            }
        }

        public IEnumerable<string> GetConnectionIds(string groupId)
        {
            lock (_lock)
            {
                return _groupSubscriptions.TryGetValue(groupId, out var connections)
                    ? connections.ToList()
                    : Enumerable.Empty<string>();
            }
        }

        public void ClearConnection(string connectionId)
        {
            lock (_lock)
            {
                var groupsToRemove = _groupSubscriptions
                    .Where(kvp => kvp.Value.Contains(connectionId))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var groupId in groupsToRemove)
                {
                    RemoveSubscription(groupId, connectionId);
                }
            }
        }
    }
} 