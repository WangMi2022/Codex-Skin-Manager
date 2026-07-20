import { access, mkdir, readFile, rename, unlink, writeFile } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { execFile } from 'node:child_process'
import { dirname, extname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

const scriptDir = dirname(fileURLToPath(import.meta.url))
const execFileAsync = promisify(execFile)

async function loadEnv(file) {
	if (!existsSync(file)) return
	const content = await readFile(file, 'utf8')
	for (const line of content.split(/\r?\n/)) {
		const value = line.trim()
		if (!value || value.startsWith('#')) continue
		const separator = value.indexOf('=')
		if (separator < 1) continue
		const key = value.slice(0, separator).trim()
		const item = value.slice(separator + 1).trim().replace(/^["']|["']$/g, '')
		if (process.env[key] === undefined) process.env[key] = item
	}
}

await loadEnv(resolve(process.cwd(), '.env'))
await loadEnv(resolve(scriptDir, 'generate-image2.env'))

function usage() {
	console.log(`Usage:
  node generate-image2.mjs --prompt-file <file> --output <png> [options]
  node generate-image2.mjs --prompt <text> --output <png> [options]

Options:
  --base-url <url>    Image API base URL (IMAGE2_BASE_URL or OPENAI_BASE_URL)
  --model <name>      Image model (default: OPENAI_IMAGE_MODEL or gpt-image-2)
  --size <WxH>        Final PNG size (default: 2560x1440)
  --request-size <WxH> Size sent to the relay (default: same as --size)
  --quality <value>   low, medium, high, or auto (default: high)
  --timeout <seconds> Request timeout (default: 600)
  --focus-x <0..1>    Horizontal focus for automatic cover crop (default: 0.5)
  --focus-y <0..1>    Vertical focus for automatic cover crop (default: 0.5)
  --stream            Request partial-image SSE events to avoid proxy timeouts
  --strict-size       Fail instead of normalizing a relay size mismatch
  --force             Replace an existing output file
  --dry-run           Validate and print the request without sending it
  --help              Show this help

Credentials:
  Set IMAGE2_API_KEY (preferred) or OPENAI_API_KEY in cwd/.env or
  windows/scripts/generate-image2.env. Do not pass keys on the command line.`)
}

function readOption(argv, index, name) {
	if (index + 1 >= argv.length) throw new Error(`${name} requires a value`)
	return argv[index + 1]
}

function parseArgs(argv) {
	const options = {
		baseUrl: process.env.IMAGE2_BASE_URL || process.env.OPENAI_BASE_URL || 'https://sub.hookai.shop/v1',
		model: process.env.OPENAI_IMAGE_MODEL || 'gpt-image-2',
		size: '2560x1440',
		requestSize: '',
		quality: 'high',
		timeoutSeconds: 600,
		focusX: 0.5,
		focusY: 0.5,
		prompt: '',
		promptFile: '',
		output: '',
		force: false,
		stream: false,
		strictSize: false,
		dryRun: false,
		help: false
	}

	for (let index = 0; index < argv.length; index += 1) {
		const name = argv[index]
		switch (name) {
			case '--base-url': options.baseUrl = readOption(argv, index++, name); break
			case '--model': options.model = readOption(argv, index++, name); break
			case '--size': options.size = readOption(argv, index++, name); break
			case '--request-size': options.requestSize = readOption(argv, index++, name); break
			case '--quality': options.quality = readOption(argv, index++, name); break
			case '--timeout': options.timeoutSeconds = Number(readOption(argv, index++, name)); break
			case '--focus-x': options.focusX = Number(readOption(argv, index++, name)); break
			case '--focus-y': options.focusY = Number(readOption(argv, index++, name)); break
			case '--prompt': options.prompt = readOption(argv, index++, name); break
			case '--prompt-file': options.promptFile = readOption(argv, index++, name); break
			case '--output':
			case '--out': options.output = readOption(argv, index++, name); break
			case '--force': options.force = true; break
			case '--stream': options.stream = true; break
			case '--strict-size': options.strictSize = true; break
			case '--dry-run': options.dryRun = true; break
			case '--help':
			case '-h': options.help = true; break
			default: throw new Error(`Unknown option: ${name}`)
		}
	}
	return options
}

function validateOptions(options) {
	if (options.help) return
	if ((!options.prompt && !options.promptFile) || (options.prompt && options.promptFile)) {
		throw new Error('Provide exactly one of --prompt or --prompt-file')
	}
	if (!options.output) throw new Error('--output is required')
	if (!/^https?:\/\//i.test(options.baseUrl)) throw new Error('--base-url must use HTTP or HTTPS')
	if (!/^gpt-image-[A-Za-z0-9._-]+$/i.test(options.model)) throw new Error('Only gpt-image-* models are supported')
	if (!/^\d{2,5}x\d{2,5}$/i.test(options.size)) throw new Error('--size must use WIDTHxHEIGHT')
	if (options.requestSize && !/^\d{2,5}x\d{2,5}$/i.test(options.requestSize)) {
		throw new Error('--request-size must use WIDTHxHEIGHT')
	}
	if (!['low', 'medium', 'high', 'auto'].includes(options.quality)) throw new Error('Invalid --quality value')
	if (!Number.isFinite(options.timeoutSeconds) || options.timeoutSeconds < 10 || options.timeoutSeconds > 3600) {
		throw new Error('--timeout must be between 10 and 3600 seconds')
	}
	if (!Number.isFinite(options.focusX) || options.focusX < 0 || options.focusX > 1 ||
		!Number.isFinite(options.focusY) || options.focusY < 0 || options.focusY > 1) {
		throw new Error('--focus-x and --focus-y must be between 0 and 1')
	}
	if (extname(options.output).toLowerCase() !== '.png') throw new Error('--output must end in .png')
}

async function fileExists(file) {
	try { await access(file); return true } catch { return false }
}

function pngDimensions(buffer) {
	const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])
	if (buffer.length < 24 || !buffer.subarray(0, 8).equals(signature)) return null
	return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) }
}

async function responsePayload(response) {
	const raw = await response.text()
	try { return JSON.parse(raw) } catch { return { raw } }
}

async function imageFromEventStream(response) {
	if (!response.body) throw new Error('Streaming response has no body')
	const decoder = new TextDecoder()
	let pending = ''
	let latest = null

	function consumeEvent(block) {
		const data = block.split(/\r?\n/)
			.filter(line => line.startsWith('data:'))
			.map(line => line.slice(5).trim())
			.join('\n')
		if (!data || data === '[DONE]') return
		let event
		try { event = JSON.parse(data) } catch { return }
		if ((event.type === 'image_generation.partial_image' || event.type === 'image_generation.completed') && event.b64_json) {
			latest = Buffer.from(event.b64_json, 'base64')
			const stage = event.type === 'image_generation.completed' ? 'completed image' : `partial image ${event.partial_image_index + 1}`
			console.log(`received ${stage}`)
		}
		if (event.type === 'error') throw new Error(event.error?.message || event.message || 'Image stream failed')
	}

	for await (const chunk of response.body) {
		pending += decoder.decode(chunk, { stream: true })
		const blocks = pending.split(/\r?\n\r?\n/)
		pending = blocks.pop() || ''
		for (const block of blocks) consumeEvent(block)
	}
	pending += decoder.decode()
	if (pending.trim()) consumeEvent(pending)
	if (!latest) throw new Error('Image stream completed without image data')
	return latest
}

async function normalizePng(input, output, width, height, focusX, focusY) {
	const python = process.env.IMAGE2_PYTHON || 'python'
	const helper = resolve(scriptDir, 'normalize-image2.py')
	try {
		await execFileAsync(python, [helper, '--input', input, '--output', output,
			'--width', String(width), '--height', String(height),
			'--focus-x', String(focusX), '--focus-y', String(focusY)], {
			windowsHide: true,
			timeout: 120000,
			maxBuffer: 1024 * 1024
		})
	} catch (error) {
		const detail = (error.stderr || error.stdout || error.message || '').trim()
		throw new Error(`Automatic size normalization failed: ${detail}`)
	}
}

async function generate(options) {
	const prompt = options.promptFile
		? (await readFile(resolve(options.promptFile), 'utf8')).trim()
		: options.prompt.trim()
	if (!prompt) throw new Error('Prompt is empty')

	const output = resolve(options.output)
	if (!options.force && await fileExists(output)) throw new Error(`Output already exists: ${output}`)
	const endpoint = `${options.baseUrl.replace(/\/+$/, '')}/images/generations`
	const request = {
		model: options.model,
		prompt,
		size: options.requestSize || options.size,
		quality: options.quality
	}
	if (options.stream) {
		request.stream = true
		request.partial_images = 3
		request.output_format = 'png'
	} else {
		request.response_format = 'b64_json'
	}

	if (options.dryRun) {
		console.log(JSON.stringify({ endpoint, output, final_size: options.size, ...request }, null, 2))
		return
	}

	const apiKey = process.env.IMAGE2_API_KEY || process.env.OPENAI_API_KEY || ''
	if (!apiKey) throw new Error('IMAGE2_API_KEY or OPENAI_API_KEY is not set')
	const controller = new AbortController()
	const timer = setTimeout(() => controller.abort(), options.timeoutSeconds * 1000)
	let response
	try {
		response = await fetch(endpoint, {
			method: 'POST',
			headers: {
				'content-type': 'application/json',
				authorization: `Bearer ${apiKey}`
			},
			body: JSON.stringify(request),
			signal: controller.signal
		})
	} finally {
		clearTimeout(timer)
	}

	if (!response.ok) {
		const payload = await responsePayload(response)
		if (response.status === 524 || /error code 524|a timeout occurred/i.test(payload?.raw || '')) {
			throw new Error('Relay timed out (HTTP 524). Retry with --quality medium or low.')
		}
		throw new Error(payload?.error?.message || payload?.message || payload?.raw || `HTTP ${response.status}`)
	}
	let buffer
	if (options.stream) {
		buffer = await imageFromEventStream(response)
	} else {
		const payload = await responsePayload(response)
		const item = payload?.data?.[0]
		if (item?.b64_json) {
			buffer = Buffer.from(item.b64_json, 'base64')
		} else if (item?.url) {
			const download = await fetch(item.url)
			if (!download.ok) throw new Error(`Image download failed: HTTP ${download.status}`)
			buffer = Buffer.from(await download.arrayBuffer())
		} else {
			throw new Error('Response has neither data[0].b64_json nor data[0].url')
		}
	}

	const dimensions = pngDimensions(buffer)
	if (!dimensions) throw new Error('Response is not a PNG image')
	const [expectedWidth, expectedHeight] = options.size.split('x').map(Number)
	await mkdir(dirname(output), { recursive: true })
	const temporaryBase = `${output}.${process.pid}.${Date.now()}`
	const sourceTemporary = `${temporaryBase}.source.png`
	const finalTemporary = `${temporaryBase}.final.png`
	try {
		await writeFile(sourceTemporary, buffer)
		const sizeMatches = dimensions.width === expectedWidth && dimensions.height === expectedHeight
		if (!sizeMatches && options.strictSize) {
			throw new Error(`Image size mismatch: expected ${options.size}, received ${dimensions.width}x${dimensions.height}`)
		}
		if (sizeMatches) {
			await rename(sourceTemporary, finalTemporary)
		} else {
			console.warn(`relay returned ${dimensions.width}x${dimensions.height}; normalizing to ${options.size}`)
			await normalizePng(sourceTemporary, finalTemporary, expectedWidth, expectedHeight, options.focusX, options.focusY)
		}
		if (options.force && await fileExists(output)) await unlink(output)
		await rename(finalTemporary, output)
	} catch (error) {
		try { await unlink(finalTemporary) } catch { }
		throw error
	} finally {
		try { await unlink(sourceTemporary) } catch { }
	}
	console.log(`generated ${output} (${expectedWidth}x${expectedHeight})`)
}

try {
	const options = parseArgs(process.argv.slice(2))
	validateOptions(options)
	if (options.help) usage()
	else await generate(options)
} catch (error) {
	console.error(`failed: ${error.message}`)
	process.exitCode = 1
}
