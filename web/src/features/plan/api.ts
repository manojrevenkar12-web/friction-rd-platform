import { api } from "../../shared/api";

export type Dispatch = { benchId: string; orderId: string; changeover: boolean; start: string; end: string; reason: string };
export type Schedule = { policy: string; generatedAt: string; changeovers: number; late: number; unplanned: number; dispatches: Dispatch[] };
export type OpenOrder = { id: string; formulation: string; application: string; procedure: string; deadline: string | null; priority: string };

export const getSchedule = (policy: string) => api<Schedule>(`/api/schedule?policy=${policy}`);
export const getOrders = () => api<OpenOrder[]>("/api/orders");
export const getBenches = () => api<{ id: string; kind: string }[]>("/api/master/benches");
