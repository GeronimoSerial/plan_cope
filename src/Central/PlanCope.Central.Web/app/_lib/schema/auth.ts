import { z } from "zod";

export const loginSchema = z.object({
  username: z.string().trim().min(1, "Ingresa tu usuario."),
  password: z.string().min(1, "Ingresa tu contraseña.")
});

export type LoginValues = z.infer<typeof loginSchema>;
