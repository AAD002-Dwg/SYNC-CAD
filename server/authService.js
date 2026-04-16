const jwt = require('jsonwebtoken');
const { OAuth2Client } = require('google-auth-library');
const { google } = require('googleapis');
require('dotenv').config();

const JWT_SECRET = process.env.JWT_SECRET || 'sync-cad-super-secret-key-change-me';
const JWT_EXPIRES_IN = '7d';

const client = new OAuth2Client(
    process.env.GOOGLE_CLIENT_ID,
    process.env.GOOGLE_CLIENT_SECRET,
    // Note: Provide a valid redirect URI in frontend/postMessage or callback
    // PostMessage is preferred for React `@react-oauth/google` popup
    'postmessage' 
);

/**
 * Verifies a Google ID token received from the frontend
 */
async function verifyGoogleToken(token) {
    try {
        const ticket = await client.verifyIdToken({
            idToken: token,
            audience: process.env.GOOGLE_CLIENT_ID,
        });
        const payload = ticket.getPayload();
        return {
            googleId: payload['sub'],
            email: payload['email'],
            name: payload['name'],
            picture: payload['picture']
        };
    } catch (error) {
        console.error('Error verifying Google Token:', error);
        throw new Error('Invalid Google credentials');
    }
}

/**
 * Handles the OAuth code exchange for Studio Admin's Drive integration
 * Retrieves the refresh_token.
 */
async function exchangeCodeForTokens(code, redirectUri = 'postmessage') {
    const oauth2Client = new google.auth.OAuth2(
        process.env.GOOGLE_CLIENT_ID,
        process.env.GOOGLE_CLIENT_SECRET,
        redirectUri
    );

    const { tokens } = await oauth2Client.getToken(code);
    return tokens; // Contains access_token and refresh_token
}

/**
 * Generates our own JWT for session management
 */
function generateSessionToken(userPayload) {
    return jwt.sign(userPayload, JWT_SECRET, { expiresIn: JWT_EXPIRES_IN });
}

/**
 * Verifies our session JWT
 */
function verifySessionToken(token) {
    return jwt.verify(token, JWT_SECRET);
}

/**
 * Generates a Desktop Token (for AutoCAD)
 */
function generateDesktopToken(studioId, userName) {
    // Desktop tokens can last longer or shorter, e.g. 30 days
    return jwt.sign({ type: 'desktop', studioId, userName }, JWT_SECRET, { expiresIn: '30d' });
}

module.exports = {
    verifyGoogleToken,
    exchangeCodeForTokens,
    generateSessionToken,
    verifySessionToken,
    generateDesktopToken,
    client
};
