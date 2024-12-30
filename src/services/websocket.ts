type MessageHandler = (data: any) => void;

export class WebSocketService {
    private socket: WebSocket | null = null;
    private messageHandlers: Map<string, MessageHandler[]> = new Map();
    private reconnectAttempts = 0;
    private maxReconnectAttempts = 5;
    private reconnectDelay = 3000;

    constructor(private url: string) {}

    connect() {
        try {
            this.socket = new WebSocket(this.url);
            this.setupEventListeners();
        } catch (error) {
            console.error('WebSocket connection error:', error);
            this.handleReconnect();
        }
    }

    private setupEventListeners() {
        if (!this.socket) return;

        this.socket.onopen = () => {
            console.log('WebSocket connected');
            this.reconnectAttempts = 0;
        };

        this.socket.onmessage = (event) => {
            try {
                const message = JSON.parse(event.data);
                this.handleMessage(message);
            } catch (error) {
                console.error('Error parsing WebSocket message:', error);
            }
        };

        this.socket.onclose = () => {
            console.log('WebSocket disconnected');
            this.handleReconnect();
        };

        this.socket.onerror = (error) => {
            console.error('WebSocket error:', error);
        };
    }

    private handleReconnect() {
        if (this.reconnectAttempts < this.maxReconnectAttempts) {
            this.reconnectAttempts++;
            setTimeout(() => this.connect(), this.reconnectDelay);
        }
    }

    subscribe(type: string, handler: MessageHandler) {
        if (!this.messageHandlers.has(type)) {
            this.messageHandlers.set(type, []);
        }
        this.messageHandlers.get(type)?.push(handler);
    }

    unsubscribe(type: string, handler: MessageHandler) {
        const handlers = this.messageHandlers.get(type);
        if (handlers) {
            this.messageHandlers.set(
                type,
                handlers.filter(h => h !== handler)
            );
        }
    }

    private handleMessage(message: { type: string; payload: any }) {
        const handlers = this.messageHandlers.get(message.type);
        if (handlers) {
            handlers.forEach(handler => handler(message.payload));
        }
    }

    send(type: string, payload: any) {
        if (this.socket?.readyState === WebSocket.OPEN) {
            this.socket.send(JSON.stringify({ type, payload }));
        }
    }

    disconnect() {
        if (this.socket) {
            this.socket.close();
            this.socket = null;
        }
    }
} 