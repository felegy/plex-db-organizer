import {
	getHealth,
	getMovies,
	getMustDeleteMovies,
	markMovieMustDelete,
	runMigrate as executeMigrate,
	runSync as executeSync,
	searchMovies as executeSearch,
} from "./api.js";
import { marked } from "marked";

const DEFAULT_API_BASE_URL = "/api";
const DEFAULT_OUTPUT_PATH = "assets/csv/plex_movies.csv";
const ROUTES = new Set(["dashboard", "movies", "search", "must-delete"]);

function compareText(left, right) {
	return String(left || "").localeCompare(String(right || ""), undefined, { sensitivity: "base" });
}

export function app() {
	return {
		route: "dashboard",
		apiBaseUrl: localStorage.getItem("plex-api-base-url") || DEFAULT_API_BASE_URL,
		apiSettingsOpen: localStorage.getItem("plex-api-settings-open") === "true",
		errorMessage: "",
		successMessage: "",
		health: {
			loading: false,
			status: "unknown",
			detail: "Health has not been checked yet.",
		},
		migrateRunning: false,
		sync: {
			running: false,
			lastResult: null,
		},
		syncForm: {
			tmdbEnrich: true,
			omdbEnrich: true,
			batchSize: 10,
			outputPath: DEFAULT_OUTPUT_PATH,
		},
		movies: {
			loading: false,
			items: [],
			filter: "",
			sortBy: "title",
		},
		search: {
			loading: false,
			hasSearched: false,
			term: "",
			threshold: 60,
			extended: false,
			outputPath: DEFAULT_OUTPUT_PATH,
			results: [],
		},
		mustDelete: {
			loading: false,
			items: [],
		},
		help: {
			open: false,
			loading: false,
			error: "",
			html: "",
		},

		get filteredMovies() {
			const filter = this.movies.filter.trim().toLowerCase();
			const items = filter
				? this.movies.items.filter((movie) => String(movie.title || "").toLowerCase().includes(filter))
				: [...this.movies.items];

			return items.sort((left, right) => {
				if (this.movies.sortBy === "year") {
					return (right.year || 0) - (left.year || 0) || compareText(left.title, right.title);
				}

				if (this.movies.sortBy === "rating") {
					return (right.tmdbRating || 0) - (left.tmdbRating || 0) || compareText(left.title, right.title);
				}

				return compareText(left.title, right.title);
			});
		},

		init() {
			window.addEventListener("hashchange", () => this.handleRouteChange());
			this.handleRouteChange();
			this.checkHealth();
		},

		handleRouteChange() {
			const candidate = window.location.hash.replace(/^#\/?/, "") || "dashboard";
			this.route = ROUTES.has(candidate) ? candidate : "dashboard";

			if (this.route === "movies" && this.movies.items.length === 0 && !this.movies.loading) {
				this.loadMovies();
			}

			if (this.route === "must-delete" && this.mustDelete.items.length === 0 && !this.mustDelete.loading) {
				this.loadMustDelete();
			}
		},

		setApiSettingsOpen(event) {
			this.apiSettingsOpen = event.target.open;
			localStorage.setItem("plex-api-settings-open", String(this.apiSettingsOpen));
		},

		saveApiBaseUrl() {
			const normalized = (this.apiBaseUrl || DEFAULT_API_BASE_URL).trim().replace(/\/$/, "");
			this.apiBaseUrl = normalized || DEFAULT_API_BASE_URL;
			localStorage.setItem("plex-api-base-url", this.apiBaseUrl);
			this.setSuccess(`Saved API base URL: ${this.apiBaseUrl}`);
			this.checkHealth();
		},

		setError(message) {
			this.errorMessage = message;
			window.clearTimeout(this.errorTimer);
			this.errorTimer = window.setTimeout(() => {
				this.errorMessage = "";
			}, 7000);
		},

		clearError() {
			this.errorMessage = "";
			window.clearTimeout(this.errorTimer);
		},

		setSuccess(message) {
			this.successMessage = message;
			window.clearTimeout(this.successTimer);
			this.successTimer = window.setTimeout(() => {
				this.successMessage = "";
			}, 4000);
		},

		async checkHealth() {
			this.clearError();
			this.health.loading = true;

			try {
				const result = await getHealth(this.apiBaseUrl);
				this.health.status = result.status || "ok";
				this.health.detail = "API responded successfully.";
			} catch (error) {
				this.health.status = "error";
				this.health.detail = error.message;
				this.setError(error.message);
			} finally {
				this.health.loading = false;
			}
		},

		async runMigrate() {
			this.clearError();
			this.migrateRunning = true;

			try {
				const result = await executeMigrate(this.apiBaseUrl);
				this.setSuccess(result.message || "Migrations completed.");
			} catch (error) {
				this.setError(error.message);
			} finally {
				this.migrateRunning = false;
			}
		},

		async runSync() {
			this.clearError();
			this.sync.running = true;

			try {
				const result = await executeSync(this.apiBaseUrl, {
					tmdbEnrich: this.syncForm.tmdbEnrich,
					omdbEnrich: this.syncForm.omdbEnrich,
					batchSize: this.syncForm.batchSize,
					outputPath: this.syncForm.outputPath,
				});

				this.sync.lastResult = result;
				this.setSuccess(result.message || "Sync completed.");
				await Promise.all([this.loadMovies(true), this.loadMustDelete(true)]);
			} catch (error) {
				this.setError(error.message);
			} finally {
				this.sync.running = false;
			}
		},

		async loadMovies(force = false) {
			if (this.movies.loading || (!force && this.movies.items.length > 0)) {
				return;
			}

			this.clearError();
			this.movies.loading = true;

			try {
				this.movies.items = await getMovies(this.apiBaseUrl);
			} catch (error) {
				this.setError(error.message);
			} finally {
				this.movies.loading = false;
			}
		},

		async runSearch() {
			this.clearError();
			this.search.loading = true;
			this.search.hasSearched = true;

			try {
				this.search.results = await executeSearch(this.apiBaseUrl, {
					term: this.search.term,
					threshold: this.search.threshold,
					extended: this.search.extended,
					outputPath: this.search.outputPath,
				});
			} catch (error) {
				this.search.results = [];
				this.setError(error.message);
			} finally {
				this.search.loading = false;
			}
		},

		async loadMustDelete(force = false) {
			if (this.mustDelete.loading || (!force && this.mustDelete.items.length > 0)) {
				return;
			}

			this.clearError();
			this.mustDelete.loading = true;

			try {
				this.mustDelete.items = await getMustDeleteMovies(this.apiBaseUrl);
			} catch (error) {
				this.setError(error.message);
			} finally {
				this.mustDelete.loading = false;
			}
		},

		async markMustDelete(id) {
			this.clearError();
			const movie = this.movies.items.find((m) => m.id === id)
				?? this.search.results.find((r) => r.movie.id === id)?.movie;

			try {
				await markMovieMustDelete(this.apiBaseUrl, id);
				this.movies.items = this.movies.items.map((m) => (
					m.id === id ? { ...m, mustDelete: true } : m
				));
				this.search.results = this.search.results.map((r) => (
					r.movie.id === id ? { ...r, movie: { ...r.movie, mustDelete: true } } : r
				));
				await this.loadMustDelete(true);
				this.setSuccess(`"${movie?.title ?? id}" marked as must-delete.`);
			} catch (error) {
				this.setError(error.message);
			}
		},

		formatRating(value) {
			return typeof value === "number" && !Number.isNaN(value) ? value.toFixed(1) : "-";
		},

		async openHelp() {
			this.help.open = true;

			if (this.help.html || this.help.loading) {
				return;
			}

			this.help.loading = true;
			this.help.error = "";

			const candidates = ["./README.md", "/README.md", "/web/README.md"];

			try {
				let markdown = "";
				for (const path of candidates) {
					const response = await fetch(path, { headers: { Accept: "text/markdown,text/plain,*/*" } });
					if (!response.ok) {
						continue;
					}

					markdown = await response.text();
					const trimmed = markdown.trim();
					if (trimmed.length > 0 && !trimmed.toLowerCase().startsWith("<!doctype html")) {
						break;
					}
				}

				if (!markdown.trim()) {
					throw new Error("Help file was not found.");
				}

				this.help.html = marked.parse(markdown);
			} catch (error) {
				this.help.error = error instanceof Error ? error.message : "Failed to load help.";
			} finally {
				this.help.loading = false;
			}
		},

		closeHelp() {
			this.help.open = false;
		},
	};
}