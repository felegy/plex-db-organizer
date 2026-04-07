import Alpine from "alpinejs";
import "./style.css";
import { app } from "./app/app.js";

window.Alpine = Alpine;
window.app = app;

Alpine.start();
