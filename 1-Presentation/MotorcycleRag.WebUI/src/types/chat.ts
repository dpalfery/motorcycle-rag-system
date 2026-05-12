
export interface Message {
    id: string;
    role: 'user' | 'assistant';
    content: string;
    timestamp: Date;
    attachments?: Attachment[];
    actions?: Action[];
}

export interface Attachment {
    type: 'image' | 'file';
    url: string;
    name: string;
}

export interface Action {
    label: string;
    type: 'link' | 'callback';
    value: string; // URL or payload
    icon?: string; // Icon name
    reason?: string;
    subject?: string;
}
