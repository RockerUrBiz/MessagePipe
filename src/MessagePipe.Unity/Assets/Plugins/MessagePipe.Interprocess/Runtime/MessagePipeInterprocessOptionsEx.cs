namespace MessagePipe.Interprocess;

public class MessagePipeInterprocessOptionsEx : MessagePipeInterprocessOptions
{
    // Flag to enable or disable socket pooling
    public bool EnableSocketPooling { get; set; } = false;

    // Initial capacity of the socket pool
    public int InitialPoolCapacity { get; set; } = 10;

    // Maximum number of items allowed in the socket pool
    public int MaxPoolSize { get; set; } = 100;

    // Minimum number of items to retain in the socket pool
    public int MinPoolSize { get; set; } = 5;

    // Maximum time a socket can be idle in the pool before being removed
    public System.TimeSpan MaxIdleTime { get; set; }

    // Interval at which the pool checks and removes idle sockets
    public System.TimeSpan ContractionInterval { get; set; }

    // Constructor for initializing default settings
    protected MessagePipeInterprocessOptionsEx() : base()
    {
        // Call to the base class constructor
        // This will initialize any default settings defined in the base class

        // Initialize extended properties
        EnableSocketPooling = true; // Enable socket pooling by default
        InitialPoolCapacity = 10;   // Default initial capacity of the socket pool
        MaxPoolSize         = 100;  // Default maximum size of the socket pool
        MaxIdleTime         = System.TimeSpan.FromMinutes(5);
        ContractionInterval = System.TimeSpan.FromMinutes(1);
        MinPoolSize         = 5;                              // Default minimum size of the socket pool
        MaxIdleTime         = System.TimeSpan.FromMinutes(5); // Default maximum idle time for sockets in the pool
        ContractionInterval = System.TimeSpan.FromMinutes(1); // Default interval for checking and removing idle sockets

        // Other default settings can be added here
        // For example, default settings for serialization, instance lifetime, etc.
    }
}