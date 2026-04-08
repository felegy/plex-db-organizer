function normalizeBaseUrl(baseUrl) {
	const fallback = "/api";
	const candidate = (baseUrl || "").trim() || fallback;
	const normalizedCandidate = candidate.replace(/\/$/, "");

	if (typeof window === "undefined" || !window.location?.origin) {
		return normalizedCandidate || fallback;
	}

	try {
		const resolved = new URL(candidate, window.location.origin);
		const normalizedPath = resolved.pathname === "/" ? fallback : resolved.pathname;
		const pathWithQuery = `${normalizedPath}${resolved.search}${resolved.hash}`.replace(/\/$/, "");
		const isExplicitAbsolute = /^[a-z][a-z\d+\-.]*:\/\//i.test(candidate) || candidate.startsWith("//");

		if (!isExplicitAbsolute || resolved.origin === window.location.origin) {
			return pathWithQuery || "/";
		}

		return `${resolved.origin}${pathWithQuery}`.replace(/\/$/, "");
	} catch {
		return fallback;
	}
}

async function parseResponse(response) {
	const contentType = response.headers.get("content-type") || "";
	const isJson = contentType.includes("application/json") || contentType.includes("application/problem+json");

	if (isJson) {
		return response.json();
	}

	return response.text();
}

function getErrorMessage(payload, fallback) {
	if (!payload) {
		return fallback;
	}

	if (typeof payload === "string") {
		return payload;
	}

	if (typeof payload.detail === "string" && payload.detail.length > 0) {
		return payload.detail;
	}

	if (payload.errors && typeof payload.errors === "object") {
		const firstEntry = Object.values(payload.errors).find(Array.isArray);
		if (firstEntry?.length) {
			return firstEntry[0];
		}
	}

	if (typeof payload.title === "string" && payload.title.length > 0) {
		return payload.title;
	}

	return fallback;
}

async function request(baseUrl, path, options = {}) {
	const response = await fetch(`${normalizeBaseUrl(baseUrl)}${path}`, {
		headers: {
			Accept: "application/json",
			...(options.body ? { "Content-Type": "application/json" } : {}),
			...options.headers,
		},
		...options,
	});

	const payload = await parseResponse(response);

	if (!response.ok) {
		throw new Error(getErrorMessage(payload, `Request failed with status ${response.status}.`));
	}

	return payload;
}

export function getHealth(baseUrl) {
	return request(baseUrl, "/health");
}

export function runMigrate(baseUrl) {
	return request(baseUrl, "/migrate", { method: "POST" });
}

export function runSync(baseUrl, syncRequest) {
	return request(baseUrl, "/sync", {
		method: "POST",
		body: JSON.stringify(syncRequest),
	});
}

export function getMovies(baseUrl) {
	return request(baseUrl, "/movies");
}

export function searchMovies(baseUrl, params) {
	const searchParams = new URLSearchParams();
	searchParams.set("term", params.term);
	searchParams.set("threshold", String(params.threshold));
	searchParams.set("extended", String(Boolean(params.extended)));

	if (params.outputPath?.trim()) {
		searchParams.set("outputPath", params.outputPath.trim());
	}

	return request(baseUrl, `/search?${searchParams.toString()}`);
}

export function markMovieMustDelete(baseUrl, id) {
	return request(baseUrl, `/movies/${id}/must-delete`, { method: "POST" });
}

export function getMustDeleteMovies(baseUrl) {
	return request(baseUrl, "/movies/must-delete");
}